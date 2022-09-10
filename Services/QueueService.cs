using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using RCL.Logging;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Services;


public abstract class QueueService<T> : PlatformMongoTimerService<QueueService<T>.TaskData> where T : PlatformDataModel
{
    private const int MAX_FAILURE_COUNT = 5;
    private const int MS_TAKEOVER = 30_000;
    private const int RETENTION_MS = 7 * 24 * 60 * 60 * 1000; // 7 days
    private string Id { get; init; }
    protected bool IsPrimary { get; private set; }
    
    private int PrimaryTaskCount { get; init; }
    private int SecondaryTaskCount { get; init; }

    private readonly IMongoCollection<QueueConfig> _config;
    private readonly IMongoCollection<QueuedTask> _work;

    /// <summary>
    /// Creates and starts a new QueueService on startup.
    /// </summary>
    /// <param name="primaryNodeTaskCount">The number of tasks to attempt on every Elapsed timer event on the primary node.</param>
    /// <param name="secondaryNodeTaskCount">The number of tasks to attempt on every Elapsed timer event on the secondary node.  0 == unlimited.</param>
    protected QueueService([Range(1, int.MaxValue)] int primaryNodeTaskCount, [Range(0, int.MaxValue)] int secondaryNodeTaskCount = 0) 
        : base(collection: "tasks", intervalMs: 5_000, startImmediately: true)
    {
        Id = Guid.NewGuid().ToString();

        _config = new MongoClient(PlatformEnvironment.MongoConnectionString)
            .GetDatabase(PlatformEnvironment.MongoDatabaseName)
            .GetCollection<QueueConfig>("tasks");
        
        _work = new MongoClient(PlatformEnvironment.MongoConnectionString)
            .GetDatabase(PlatformEnvironment.MongoDatabaseName)
            .GetCollection<QueuedTask>("tasks");

        PrimaryTaskCount = Math.Max(1, primaryNodeTaskCount);
        SecondaryTaskCount = secondaryNodeTaskCount == 0
            ? int.MaxValue
            : secondaryNodeTaskCount;
        
        UpsertConfig();
    }

    protected override void OnElapsed()
    {
        IsPrimary = TryUpdateConfig();
        if (IsPrimary)
        {
            try
            {
                Task.Run(PrimaryNodeWork).Wait();
                
                Log.Local(Owner.Default, $"(Primary) Processing up to {PrimaryTaskCount} tasks");
                for (int count = PrimaryTaskCount; count > 0; count--)
                    if (!WorkPerformed(StartNewTask()))
                        break;

                DeleteOldTasks();
            }
            catch (Exception e)
            {
                Log.Error(Owner.Default, "Error executing primary node work", exception: e);
            }
        }
        else
        {
            Log.Local(Owner.Will, "Ready to perform work.");

            for (int count = SecondaryTaskCount; count > 0; count--)
                if (!(WorkPerformed(StartNewTask()) || WorkPerformed(RetryTask())))
                    break;
        }
    }

    /// <summary>
    /// Creates a task that a worker thread can then claim and process.
    /// </summary>
    /// <param name="data">The data object necessary to perform work on the task.</param>
    protected void CreateTask(T data) => InsertTask(data);

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

        config.Settings ??= new GenericData();
        config.Settings[key] = value;
        _config.ReplaceOne(filter: Builders<QueueConfig>.Filter.Eq(queue => queue.Id, config.Id), replacement: config);
    }

    /// <summary>
    /// Gets a value from the service's settings object.
    /// </summary>
    /// <param name="key">The key of the object to retrieve.</param>
    /// <param name="require">If true, leverages the GenericData.Require() method; otherwise OptionalT().</param>
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
    private void InsertTask(T data = null) => _work.InsertOneAsync(new QueuedTask
    {
        Data = data,
        Type = TaskData.TaskType.Work,
        Status = QueuedTask.TaskStatus.NotStarted
    });
    
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
    
    /// <summary>
    /// Marks a task as completed or failed.  If failed, the failure count is incremented.
    /// </summary>
    /// <param name="queuedTask">The task that was completed.</param>
    /// <param name="success">Determines whether or not the task is marked as successful or failed, along with the failure increment.</param>
    /// <returns>True if the record was updated.  If a record wasn't modified, something else took it, which is
    /// indicative of an error.</returns>
    private bool UpdateTaskStatus(QueuedTask queuedTask, bool success) => _work.UpdateOne(
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
                Builders<QueueConfig>.Update.Set(config => config.Settings, new GenericData())
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
        
        [BsonElement(KEY_PRIMARY_ID)]
        internal string PrimaryServiceId { get; set; }
        
        [BsonElement(KEY_ACTIVITY)]
        internal long LastActive { get; set; }
        
        [BsonElement(KEY_SETTINGS)]
        public GenericData Settings { get; set; }
    }
    
    [BsonIgnoreExtraElements]
    private class QueuedTask : TaskData
    {
        private const string KEY_CLAIMED_BY = "owner";
        private const string KEY_CLAIMED_ON = "taken";
        private const string KEY_DATA = "data";
        private const string KEY_FAILURES = "failures";
        private const string KEY_STATUS = "status";
        
        [BsonElement(KEY_CLAIMED_BY)]
        public string ClaimedBy { get; set; }
        
        [BsonElement(KEY_CLAIMED_ON)]
        public long ClaimedOn { get; set; }
        
        [BsonElement(KEY_STATUS)]
        internal TaskStatus Status { get; set; }
        
        [BsonElement(KEY_DATA)]
        internal T Data { get; set; }
        
        [BsonElement(KEY_FAILURES)]
        internal int Failures { get; set; }
        
        internal enum TaskStatus
        {
            NotStarted = 0,
            Claimed = 100,
            Succeeded = 200,
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
}