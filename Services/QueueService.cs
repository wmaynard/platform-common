using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using RCL.Logging;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Services;


public abstract class QueueService<T> : PlatformMongoTimerService<QueueService<T>.TaskData> where T : PlatformDataModel
{
    private const int MAX_FAILURE_COUNT = 5;
    private const int MS_TAKEOVER = 15 * 60 * 1000; // 15 minutes
    private const int RETENTION_MS = 7 * 24 * 60 * 60 * 1000; // 7 days
    private const string COLLECTION_PREFIX = "queue_";
    protected string Id { get; init; }
    protected bool IsPrimary { get; private set; }
    
    private int PrimaryTaskCount { get; init; }
    private int SecondaryTaskCount { get; init; }

    private readonly IMongoCollection<QueueConfig> _config;
    private readonly IMongoCollection<QueuedTask> _work;

    /// <summary>
    /// Creates and starts a new QueueService on startup.
    /// </summary>
    /// <param name="collection">The collection for queued tasks.  This collection will be prepended with "queue_".</param>
    /// <param name="intervalMs">The length of time between PrimaryWork() calls.  The timer is paused while the thread is working.</param>
    /// <param name="primaryNodeTaskCount">The number of tasks to attempt on every Elapsed timer event on the primary node.</param>
    /// <param name="secondaryNodeTaskCount">The number of tasks to attempt on every Elapsed timer event on the secondary node.  0 == unlimited.</param>
    protected QueueService(string collection, int intervalMs = 5_000, [Range(1, int.MaxValue)] int primaryNodeTaskCount = 1, [Range(0, int.MaxValue)] int secondaryNodeTaskCount = 0) 
        : base(collection: $"{COLLECTION_PREFIX}{collection}", intervalMs: intervalMs, startImmediately: true)
    {
        Id = Guid.NewGuid().ToString();

        collection = $"{COLLECTION_PREFIX}{collection}";

        _config = new MongoClient(PlatformEnvironment.MongoConnectionString)
            .GetDatabase(PlatformEnvironment.MongoDatabaseName)
            .GetCollection<QueueConfig>(collection);
        
        _work = new MongoClient(PlatformEnvironment.MongoConnectionString)
            .GetDatabase(PlatformEnvironment.MongoDatabaseName)
            .GetCollection<QueuedTask>(collection);

        PrimaryTaskCount = Math.Max(1, primaryNodeTaskCount);
        SecondaryTaskCount = secondaryNodeTaskCount == 0
            ? int.MaxValue
            : secondaryNodeTaskCount;
        
        UpsertConfig();
    }

    private T[] AcknowledgeTasks()
    {
        //.Project<string>(Builders<Enrollment>.Projection.Expression(enrollment => enrollment.AccountID))
        
        T[] data = _work
            .Find(filter: task => task.Status == QueuedTask.TaskStatus.Succeeded)
            .Project<T>(Builders<QueuedTask>.Projection.Expression(task => task.Data))
            .ToList()
            .ToArray();
        
        long affected = _work.UpdateMany(
            filter: task => task.Status == QueuedTask.TaskStatus.Succeeded,
            update: Builders<QueuedTask>.Update.Set(task => task.Status, QueuedTask.TaskStatus.Acknowledged)
        ).ModifiedCount;
        
        Log.Local(Owner.Default, $"Acknowledged {affected} tasks.");

        return data;
    }

    protected sealed override void OnElapsed()
    {
        IsPrimary = TryUpdateConfig();
        if (IsPrimary)
        {
            try
            {
                Task.Run(PrimaryNodeWork).Wait();
                
                for (int count = PrimaryTaskCount; count > 0; count--)
                    if (!WorkPerformed(StartNewTask()))
                        break;

                bool completed = _config.UpdateOne(
                    filter: Builders<QueueConfig>.Filter.And(
                        Builders<QueueConfig>.Filter.Lte(config => config.OnCompleteTime, Timestamp.UnixTime),
                        Builders<QueueConfig>.Filter.SizeLte(config => config.Waitlist, 0)
                    ),
                    update: Builders<QueueConfig>.Update.Set(config => config.OnCompleteTime, -1)
                ).ModifiedCount > 0;
                
                if (completed)
                    try
                    {
                        OnTasksCompleted(AcknowledgeTasks());
                    }
                    catch (Exception e)
                    {
                        Log.Error(Owner.Default, "Could not successfully execute OnTasksCompleted().", exception: e);
                    }
                
                AbandonOldTrackedTasks();
                DeleteOldTasks();
            }
            catch (Exception e)
            {
                Log.Error(Owner.Default, "Error executing primary node work", exception: e);
            }
        }
        else
            for (int count = SecondaryTaskCount; count > 0; count--)
                if (!(WorkPerformed(StartNewTask()) || WorkPerformed(RetryTask())))
                    break;
    }

    /// <summary>
    /// Creates a task that a worker thread can then claim and process.  Once all tasks are completed, the next Primary
    /// cycle will execute OnTasksCompleted().
    /// </summary>
    /// <param name="data">The data object necessary to perform work on the task.</param>
    protected void CreateTask(T data) => InsertTask(data, track: true);
    
    /// <summary>
    /// Creates a task that a worker thread can then claim and process.  This does not trigger OnTasksCompleted();
    /// </summary>
    /// <param name="data">The data object necessary to perform work on the task.</param>
    protected void CreateUntrackedTask(T data) => InsertTask(data, track: false);

    /// <summary>
    /// 
    /// </summary>
    protected abstract void OnTasksCompleted(T[] data);

    /// <summary>
    /// Actions to perform on the primary - and only the primary - node.  Secondary nodes will not perform this work.
    /// Use this method to call CreateTask(); apply your conditionals of logic here and actually complete those tasks
    /// in the ProcessTask() method.
    /// </summary>
    protected abstract void PrimaryNodeWork();
    
    /// <summary>
    /// Actions to perform on task data.  It is STRONGLY advised that you do not create tasks here carelessly, as doing so
    /// could result in creating more tasks than you can possibly process.  This would result in endless MongoDB data
    /// generation.
    /// </summary>
    /// <param name="data">The data from the previously-called CreateTask() method.</param>
    protected abstract void ProcessTask(T data);

    /// <summary>
    /// Sets a value in the service's settings object for later user.
    /// </summary>
    /// <param name="key">The key of the object to set.</param>
    /// <param name="value">The value to store.</param>
    protected void Set(string key, object value)
    {
        QueueConfig config = _config.Find(config => config.Type == TaskData.TaskType.Config).FirstOrDefault();
        if (config == null)
        {
            Log.Error(Owner.Default, "QueueService config not found.");
            return;
        }

        config.Settings ??= new RumbleJson();
        config.Settings[key] = value;
        _config.ReplaceOne(filter: Builders<QueueConfig>.Filter.Eq(queue => queue.Id, config.Id), replacement: config);
    }

    /// <summary>
    /// Gets a value from the service's settings object.
    /// </summary>
    /// <param name="key">The key of the object to retrieve.</param>
    /// <param name="require">If true, leverages the RumbleJson.Require() method; otherwise OptionalT().</param>
    /// <typeparam name="U">The type to cast the object to on return.</typeparam>
    /// <returns>A value from the service's settings object.</returns>
    protected U Get<U>(string key, bool require = false)
    {
        QueueConfig config = _config.Find(config => config.Type == TaskData.TaskType.Config).FirstOrDefault();

        if (require && config?.Settings == null)
            throw new PlatformException("QueueService config not found.", code: ErrorCode.MongoRecordNotFound);

        return require
            ? config.Settings.Require<U>(key)
            : config.Settings.Optional<U>(key);
    }

    /// <summary>
    /// Lays claim to an outstanding task.  Will return null if no task is available.
    /// </summary>
    /// <param name="filter">The filter to find a specific task.</param>
    /// <returns>A QueuedTask, if available, otherwise null.</returns>
    private async Task<QueuedTask> ClaimTask(FilterDefinition<QueuedTask> filter) => await _work.FindOneAndUpdateAsync(
        filter: filter,
        update: Builders<QueuedTask>.Update.Combine(
            Builders<QueuedTask>.Update.Set(field: task => task.ClaimedBy, Id),
            Builders<QueuedTask>.Update.Set(field: task => task.ClaimedOn, Timestamp.UnixTimeMS),
            Builders<QueuedTask>.Update.Set(field: task => task.Status, QueuedTask.TaskStatus.Claimed)
    ));

    /// <summary>
    /// Marks a task as completed.
    /// </summary>
    /// <param name="queuedTask">The task that was completed.</param>
    /// <returns>True if the record was updated.  If a record wasn't modified, something else took it, which is
    /// indicative of an error.</returns>
    private bool CompleteTask(QueuedTask queuedTask) => UpdateTaskStatus(queuedTask, success: true);

    /// <summary>
    /// Removes tasks that are old and no longer relevant.
    /// </summary>
    private void DeleteOldTasks() => _work.DeleteMany(filter: Builders<QueuedTask>.Filter.And(
        Builders<QueuedTask>.Filter.Eq(task => task.Type, TaskData.TaskType.Work),
        Builders<QueuedTask>.Filter.Eq(task => task.Status, QueuedTask.TaskStatus.Succeeded),
        Builders<QueuedTask>.Filter.Lte(task => task.CreatedOn, Timestamp.UnixTimeMS - RETENTION_MS)
    ));

    /// <summary>
    /// Marks a task as completed.
    /// </summary>
    /// <param name="queuedTask">The task that was completed.</param>
    /// <returns>True if the record was updated.  If a record wasn't modified, something else took it, which is
    /// indicative of an error.</returns>
    private bool FailTask(QueuedTask queuedTask) => UpdateTaskStatus(queuedTask, success: false);

    /// <summary>
    /// Creates a task that can then be claimed by worker threads.
    /// </summary>
    /// <param name="data">The data needed to perform work on the task.</param>
    /// <param name="track">If true, the config will execute OnTasksCompleted() once all tracked tasks are processed.</param>
    private async void InsertTask(T data = null, bool track = false)
    {
        QueuedTask document = new QueuedTask
        {
            Data = data,
            Type = TaskData.TaskType.Work,
            Status = QueuedTask.TaskStatus.NotStarted,
            Tracked = track
        };
        await _work.InsertOneAsync(document);

        if (!track)
            return;
        
        // Add the tracked task's ID to the config's waitlist.
        // Set the minimum OnCompleteTime for the next Primary cycle.  
        QueueConfig config = await _config.FindOneAndUpdateAsync<QueueConfig>(
            filter: config => config.OnCompleteTime <= 0,
            update: Builders<QueueConfig>.Update
                .Push(config => config.Waitlist, document.Id)
                .Set(config => config.OnCompleteTime, Timestamp.UnixTime + (IntervalMs / 1_000)),
            options: new FindOneAndUpdateOptions<QueueConfig>
            {
                ReturnDocument = ReturnDocument.After
            }
        ) ?? await _config.FindOneAndUpdateAsync<QueueConfig>(                                      // If config is null, the update didn't take
            filter: config => true,                                                                 // effect; this is because the OnCompleteTime
            update: Builders<QueueConfig>.Update.Push(config => config.Waitlist, document.Id),  // was set by a previous task
            options: new FindOneAndUpdateOptions<QueueConfig>
            {
                ReturnDocument = ReturnDocument.After
            }
        );
        Log.Local(Owner.Default, $"Tracked tasks: {config.Waitlist.Count}");
    }
    
    /// <summary>
    /// Attempts to start a previously failed task.
    /// </summary>
    /// <returns>True if a task was found.</returns>
    private async Task<bool> RetryTask() => await TryTask(Builders<QueuedTask>.Filter.And(
        Builders<QueuedTask>.Filter.Eq(task => task.Status, QueuedTask.TaskStatus.Failed),
        Builders<QueuedTask>.Filter.Lte(task => task.Failures, MAX_FAILURE_COUNT)
    ));
    
    /// <summary>
    /// Attempts to start a new, previously unattempted task.
    /// </summary>
    /// <returns>True if a task was found.</returns>
    private async Task<bool> StartNewTask() => await TryTask(Builders<QueuedTask>.Filter.And(
        Builders<QueuedTask>.Filter.Eq(field: task => task.ClaimedBy, null),
        Builders<QueuedTask>.Filter.Eq(field: task => task.Type, TaskData.TaskType.Work)
    ));
    
    /// <summary>
    /// Begins work on a task.  If the task is null, no work can be done.  Note that this returns true even if the task fails.
    /// </summary>
    /// <param name="filter">The filter definition to find the task you want to claim and attempt.</param>
    /// <returns>True if work was performed, otherwise false.  The task was not necessarily successful even when returning true.</returns>
    private async Task<bool> TryTask(FilterDefinition<QueuedTask> filter)
    {
        QueuedTask task = await ClaimTask(filter);

        if (task == null)
            return false;
        
        try
        {
            ProcessTask(task.Data);
            if (!CompleteTask(task))
                throw new PlatformException("Could not mark the queuedTask as completed.", code: ErrorCode.MongoRecordNotFound);
        }
        catch (Exception e)
        {
            
            Log.Error(Owner.Default, "Unable to complete queuedTask!", data: e.Data);
            try
            {
                if (!FailTask(task))
                    throw new PlatformException("Could not mark the queuedTask as failed!", code: ErrorCode.MongoRecordNotFound);
            }
            catch (Exception nested)
            {
                Log.Critical(Owner.Default, "Could not fail queuedTask!  It was probably deleted!", exception: nested);
            }
        }

        return true;
    }
    
    /// <summary>
    /// Returns true if this service is now the Primary node.
    /// </summary>
    private bool TryUpdateConfig() => _config.UpdateOne(
        filter: Builders<QueueConfig>.Filter.And(
            Builders<QueueConfig>.Filter.Eq(config => config.Type, TaskData.TaskType.Config),
            Builders<QueueConfig>.Filter.Or(
                Builders<QueueConfig>.Filter.Eq(config => config.PrimaryServiceId, Id),
                Builders<QueueConfig>.Filter.Eq(config => config.PrimaryServiceId, null),
                Builders<QueueConfig>.Filter.Lte(config => config.LastActive, Timestamp.UnixTimeMS - MS_TAKEOVER)
            )
        ),
        update: Builders<QueueConfig>.Update.Combine(
            Builders<QueueConfig>.Update.Set(config => config.PrimaryServiceId, Id),
            Builders<QueueConfig>.Update.Set(config => config.LastActive, Timestamp.UnixTimeMS)
        )
    ).ModifiedCount > 0;

    private void AbandonOldTrackedTasks()
    {
        long affected = _config.UpdateMany(
            filter: Builders<QueueConfig>.Filter.And(
                Builders<QueueConfig>.Filter.Lte(config => config.LastTrackingTime, Timestamp.UnixTime - 1_800),
                Builders<QueueConfig>.Filter.SizeGt(config => config.Waitlist, 0)
            ),
            update: Builders<QueueConfig>.Update.Set(config => config.Waitlist, new HashSet<string>())
        ).ModifiedCount;
        
        if (affected > 0)
            Log.Error(Owner.Default, "Abandoned old tracked quests.  Processed tasks were unable to update the queue config properly.", data: new
            {
                DroppedTasks = affected
            });
    }

    /// <summary>
    /// Marks a task as completed or failed.  If failed, the failure count is incremented.
    /// </summary>
    /// <param name="queuedTask">The task that was completed.</param>
    /// <param name="success">Determines whether or not the task is marked as successful or failed, along with the failure increment.</param>
    /// <returns>True if the record was updated.  If a record wasn't modified, something else took it, which is
    /// indicative of an error.</returns>
    private bool UpdateTaskStatus(QueuedTask queuedTask, bool success)
    {
        // Update the config node and remove the task Id.
        _config.FindOneAndUpdate<QueueConfig>(
            filter: config => true,
            update: Builders<QueueConfig>.Update
                .Pull(config => config.Waitlist, queuedTask.Id)
                .Set(config => config.LastTrackingTime, Timestamp.UnixTime),
            options: new FindOneAndUpdateOptions<QueueConfig>
            {
                ReturnDocument = ReturnDocument.After
            }
        );

        return _work.UpdateOne(
            filter: Builders<QueuedTask>.Filter.And(
                Builders<QueuedTask>.Filter.Eq(task => task.Id, queuedTask.Id),
                Builders<QueuedTask>.Filter.Eq(task => task.ClaimedBy, Id)
            ),
            update: success
                ? Builders<QueuedTask>.Update.Set(task => task.Status, QueuedTask.TaskStatus.Succeeded)
                : Builders<QueuedTask>.Update.Combine(
                    Builders<QueuedTask>.Update.Set(task => task.Status, QueuedTask.TaskStatus.Failed),
                    Builders<QueuedTask>.Update.Inc(task => task.Failures, 1)
                )
        ).ModifiedCount > 0;
    }

    /// <summary>
    /// Updates the config on startup, regardless of node status.
    /// </summary>
    private void UpsertConfig()
    {
        if (_config.Find(config => config.Type == TaskData.TaskType.Config).ToList().Any())
            return;
        _config.UpdateOne(
            filter: Builders<QueueConfig>.Filter.Eq(config => config.Type, TaskData.TaskType.Config),
            update: Builders<QueueConfig>.Update.Combine(
                Builders<QueueConfig>.Update.Set(config => config.LastActive, Timestamp.UnixTimeMS),
                Builders<QueueConfig>.Update.Set(config => config.Settings, new RumbleJson())
            ),
            options: new UpdateOptions
            {
                IsUpsert = true
            }
        );
    }
    
    /// <summary>
    /// Waits on a task and returns true if work was performed.  Helper method to clean up OnElapsed().
    /// </summary>
    /// <param name="task">The TaskProcessing method.</param>
    /// <returns>True if work was performed, otherwise false.</returns>
    private static bool WorkPerformed(Task<bool> task)
    {
        task.Wait();
        return task.Result;
    }

#region Collection Documents
    [BsonIgnoreExtraElements]
    // ReSharper disable once ClassNeverInstantiated.Local
    private class QueueConfig : TaskData
    {
        private const string KEY_PRIMARY_ID = "primary";
        private const string KEY_ACTIVITY = "updated";
        private const string KEY_SETTINGS = "settings";
        private const string KEY_WAITLIST = "wait";
        private const string KEY_MINIMUM_WAIT_TIME = "waitTime";
        private const string KEY_LAST_TRACK_TIME = "lastTrackTime";
        
        [BsonElement(KEY_PRIMARY_ID)]
        internal string PrimaryServiceId { get; set; }
        
        [BsonElement(KEY_ACTIVITY)]
        internal long LastActive { get; set; }
        
        [BsonElement(KEY_MINIMUM_WAIT_TIME)]
        internal long OnCompleteTime { get; set; }
        
        [BsonElement(KEY_SETTINGS)]
        public RumbleJson Settings { get; set; }
        
        [BsonElement(KEY_WAITLIST)]
        public HashSet<string> Waitlist { get; set; }
        
        [BsonElement(KEY_LAST_TRACK_TIME)]
        public long LastTrackingTime { get; set; }

        public QueueConfig() => Waitlist = new HashSet<string>();
        
    }
    
    [BsonIgnoreExtraElements]
    private class QueuedTask : TaskData
    {
        private const string KEY_CLAIMED_BY = "owner";
        private const string KEY_CLAIMED_ON = "taken";
        private const string KEY_DATA = "data";
        private const string KEY_FAILURES = "failures";
        private const string KEY_STATUS = "status";
        private const string KEY_TRACKED = "tracked";
        
        [BsonElement(KEY_CLAIMED_BY)]
        public string ClaimedBy { get; set; }
        
        [BsonElement(KEY_CLAIMED_ON)]
        public long ClaimedOn { get; set; }
        
        [BsonElement(KEY_STATUS)]
        internal TaskStatus Status { get; set; }
        
        [BsonElement(KEY_TRACKED)]
        internal bool Tracked { get; set; }
        
        [BsonElement(KEY_DATA)]
        internal T Data { get; set; }
        
        [BsonElement(KEY_FAILURES)]
        internal int Failures { get; set; }
        
        internal enum TaskStatus
        {
            NotStarted = 0,
            Claimed = 100,
            Succeeded = 200,
            Acknowledged = 300,
            Failed = 400
        }
    }
    
    [BsonIgnoreExtraElements]
    public abstract class TaskData : PlatformCollectionDocument
    {
        private const string KEY_CREATED = "created";
        private const string KEY_TYPE = "type";

        [BsonElement(KEY_CREATED)]
        internal long CreatedOn { get; init; }
    
        [BsonElement(KEY_TYPE)]
        internal TaskType Type { get; set; }

        protected TaskData() => CreatedOn = Timestamp.UnixTimeMS;
    
        internal enum TaskType
        {
            Config = 10, 
            Work = 20
        }
    }

#endregion Collection Documents
}  // TODO: add link to health degraded slack message 