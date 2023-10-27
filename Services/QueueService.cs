using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using RCL.Logging;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Minq;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Services;

public interface IConfiscatable
{
    void Confiscate();
}

public abstract class QueueService<T> : PlatformMongoTimerService<QueueService<T>.TaskData>, IConfiscatable where T : PlatformDataModel
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
    private readonly bool _sendTaskResultsWhenTheyAreCompleted;

    /// <summary>
    /// Creates and starts a new QueueService on startup.
    /// </summary>
    /// <param name="collection">The collection for queued tasks.  This collection will be prepended with "queue_".</param>
    /// <param name="intervalMs">The length of time between PrimaryWork() calls.  The timer is paused while the thread is working.</param>
    /// <param name="primaryNodeTaskCount">The number of tasks to attempt on every Elapsed timer event on the primary node.</param>
    /// <param name="secondaryNodeTaskCount">The number of tasks to attempt on every Elapsed timer event on the secondary node.  0 == unlimited.</param>
    /// <param name="sendTaskResultsWhenTheyAreCompleted">tasks results are sent as they complete instead of all at once at the end</param>
    protected QueueService(string collection, int intervalMs = 5_000, [Range(1, int.MaxValue)] int primaryNodeTaskCount = 1, [Range(0, int.MaxValue)] int secondaryNodeTaskCount = 0, bool sendTaskResultsWhenTheyAreCompleted = false) 
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

        _sendTaskResultsWhenTheyAreCompleted = sendTaskResultsWhenTheyAreCompleted;

        PrimaryTaskCount = Math.Max(1, primaryNodeTaskCount);
        SecondaryTaskCount = secondaryNodeTaskCount == 0
            ? int.MaxValue
            : secondaryNodeTaskCount;
        
        UpsertConfig();

        if (!PlatformEnvironment.IsLocal)
            return;
        
        Log.Local(Owner.Will, "Local environment detected; newer QueueService instances always confiscate control locally.");
        Confiscate();
    }

    private T[] AcknowledgeTasks()
    {
        T[] data = _work
            .Find(filter: task => task.Status == QueuedTask.TaskStatus.Succeeded)
            .Project<T>(Builders<QueuedTask>.Projection.Expression(task => task.Data))
            .ToList()
            .ToArray();
        
        long affected = _work.UpdateMany(
            filter: task => task.Status == QueuedTask.TaskStatus.Succeeded,
            update: Builders<QueuedTask>.Update.Set(task => task.Status, QueuedTask.TaskStatus.Acknowledged)
        ).ModifiedCount;

        if (affected > 0)
            Log.Local(Owner.Default, $"Acknowledged {affected} tasks.");

        return data;
    }

    /// <summary>
    /// Immediately updates the queue's config so that this service becomes the primary node.
    /// </summary>
    public void Confiscate() => _config.UpdateOne(
        filter: config => config.Type == TaskData.TaskType.Config,
        update: Builders<QueueConfig>.Update
            .Set(config => config.PrimaryServiceId, Id)
            .Set(config => config.LastActive, Timestamp.UnixTimeMs)
    );

    protected void DeleteAcknowledgedTasks() => _work.DeleteMany(task => task.Status == QueuedTask.TaskStatus.Acknowledged);

    public long TasksRemaining() => _work.CountDocuments(filter:
        Builders<QueuedTask>.Filter.And(
            Builders<QueuedTask>.Filter.Eq(task => task.Tracked, true),
            Builders<QueuedTask>.Filter.Lte(task => task.Status, QueuedTask.TaskStatus.Claimed)
        ));

    public bool WaitingOnTaskCompletion() => _config.CountDocuments(Builders<QueueConfig>.Filter.Gt(config => config.OnCompleteTime, -1)) > 0;

    protected sealed override void OnElapsed()
    {
        IsPrimary = TryUpdateConfig();
        if (IsPrimary)
            try
            {
                Task.Run(PrimaryNodeWork).Wait();
                
                for (int count = PrimaryTaskCount; count > 0; count--)
                    if (!WorkPerformed(StartNewTask()))
                        break;

                if (TryEmptyWaitlist() || _sendTaskResultsWhenTheyAreCompleted)
                    try
                    {
                        if (!_sendTaskResultsWhenTheyAreCompleted)
                            Log.Verbose(Owner.Will, "Tasks completed.  Acknowledging tasks and firing event.");

                        T[] tasks = AcknowledgeTasks();

                        if (tasks.Length > 0)
                            OnTasksCompleted(tasks);
                    }
                    catch (Exception e)
                    {
                        Log.Error(Owner.Default, "Could not successfully execute OnTasksCompleted().", exception: e);
                    }
                
                AbandonOldTrackedTasks();
                ResetStalledTasks();
                DeleteOldTasks();
            }
            catch (Exception e)
            {
                Log.Error(Owner.Default, "Error executing primary node work", exception: e);
                if (PlatformEnvironment.IsLocal)
                    Log.Local(Owner.Default, $"({e.Message})", emphasis: Log.LogType.ERROR);
            }
        else
            for (int count = SecondaryTaskCount; count > 0; count--)
                if (!(WorkPerformed(StartNewTask()) || WorkPerformed(RetryTask())))
                    break;
    }

    /// <summary>
    /// Attempts to clear tasks from the waitlist.  Removes orphans (tasks that were deleted) and tasks that have been
    /// completed.
    /// </summary>
    /// <returns>A boolean indicating whether or not the waitlist has been emptied and the OnTasksCompleted event should fire.</returns>
    private bool TryEmptyWaitlist()
    {
        RemoveWaitlistOrphans();
        ClearSuccessfulTasks();
        
        return _config.UpdateOne(
            filter: Builders<QueueConfig>.Filter.And(
                Builders<QueueConfig>.Filter.Lte(config => config.OnCompleteTime, Timestamp.UnixTime),
                Builders<QueueConfig>.Filter.Size(config => config.Waitlist, 0)
            ),
            update: Builders<QueueConfig>.Update.Set(config => config.OnCompleteTime, -1)
        ).ModifiedCount > 0;
    } 

    private QueueConfig GetConfig() => _config
        .Find(config => true)
        .FirstOrDefault();
    
    private string[] GetWaitlist() => _config
        .Find(config => true)
        .Project(Builders<QueueConfig>.Projection.Expression(config => config.Waitlist))
        .FirstOrDefault()
        ?.ToArray()
        ?? new string[] { };

    /// <summary>
    /// If we have an active waitlist, run a query on outstanding tasks to see which tracked tasks have been completed.
    /// Remove those from the waitlist.
    /// </summary>
    private void ClearSuccessfulTasks()
    {
        // PLATF-6405: As part of a critical security patch, we upgraded from 2.13 -> 2.20.  Prior to 2.19, remote
        // code execution was possible.  In their haste to patch this, it seems that they broke multiple serializers
        // that we rely on - one of them caused this method to fall over when running a PullFilter.  Several workarounds
        // were attempted but the PullFilter appears to have been permanently broken with these models.
        // Rather than a pull filter approach, we'll load the waitlist and outstanding successful tasks in memory
        // and just use a Set operator to get around this bug in the driver.
        string[] waitlist = GetWaitlist();
        if (!waitlist.Any())
            return;
        
        string[] successes = _work
            .Find(Builders<QueuedTask>.Filter.And(
                Builders<QueuedTask>.Filter.Eq(task => task.Tracked, true),
                Builders<QueuedTask>.Filter.Eq(task => task.Status, QueuedTask.TaskStatus.Succeeded))
            )
            .Project(Builders<QueuedTask>.Projection.Expression(task => task.Id))
            .ToList()
            .ToArray();

        string[] trimmed = waitlist.Except(successes).ToArray();
        int affected = waitlist.Length - trimmed.Length;

        if (affected == 0)
            return;
        
        _config.UpdateOne(
            filter: config => true,
            update: Builders<QueueConfig>.Update.Set(config => config.Waitlist, trimmed)
        );
        
        Log.Local(Owner.Will, $"Stopped waiting on {affected} tasks.");
    }

    private void RemoveWaitlistOrphans()
    {
        string[] waitlist = GetWaitlist();
        List<string> ids = _work
            .Find(Builders<QueuedTask>.Filter.In(task => task.Id, waitlist))
            .Project(Builders<QueuedTask>.Projection.Expression(task => task.Id))
            .ToList();

        string[] orphans = waitlist
            .Except(ids)
            .ToArray();

        if (!orphans.Any())
            return;
        
        _config
            .UpdateOne(
                filter: config => true,
                update: Builders<QueueConfig>.Update.PullFilter(
                    field: config => config.Waitlist,
                    filter: Builders<string>.Filter.Where(id => orphans.Contains(id))
                )
            );
        Log.Warn(Owner.Will, "Found orphaned tasks in the waitlist.", data: new { Orphans = orphans });
    }

    /// <summary>
    /// Creates a task that a worker thread can then claim and process.  Once all tasks are completed, the next Primary
    /// cycle will execute OnTasksCompleted().
    /// </summary>
    /// <param name="data">The data object necessary to perform work on the task.</param>
    protected void CreateTask(T data) => InsertTask(data, track: true);

    protected void CreateTasks(params T[] data) => InsertTasks(true, data);
    
    /// <summary>
    /// Creates a task that a worker thread can then claim and process.  This does not trigger OnTasksCompleted();
    /// </summary>
    /// <param name="data">The data object necessary to perform work on the task.</param>
    protected void CreateUntrackedTask(T data) => InsertTask(data, track: false);

    protected void CreateUntrackedTasks(params T[] data) => InsertTasks(false, data);

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
            Builders<QueuedTask>.Update.Set(field: task => task.ClaimedOn, Timestamp.UnixTimeMs),
            Builders<QueuedTask>.Update.Set(field: task => task.Status, QueuedTask.TaskStatus.Claimed)
    ));
    
    
    /// <summary>
    /// If a server is shut down unexpectedly, tasks can get orphaned.  This method assumes that no task will take over
    /// 30 minutes to process.  If any task is marked as Claimed but has not completed after this time, this method
    /// resets the task so it can be claimed again.
    /// </summary>
    private void ResetStalledTasks()
    {
        long affected = _work.UpdateMany(
            filter: Builders<QueuedTask>.Filter.And(
                Builders<QueuedTask>.Filter.Lte(task => task.ClaimedOn, Timestamp.ThirtyMinutesAgo),
                Builders<QueuedTask>.Filter.Eq(task => task.Status, QueuedTask.TaskStatus.Claimed)
            ),
            update: Builders<QueuedTask>.Update
                .Unset(task => task.ClaimedOn)
                .Unset(task => task.ClaimedBy)
                .Set(task => task.Status, QueuedTask.TaskStatus.NotStarted)
        ).ModifiedCount;
        if (affected > 0)
            Log.Warn(Owner.Default, "A queue stalled on at least one task and reset them for processing.", data: new
            {
                Help = "This likely happened because a service instance was in the middle of processing and was forcefully shut down.  If the claiming server is still running, however, it's possible that this will result in duplication of work.",
                StalledTaskCount = affected
            });
    }

    /// <summary>
    /// Marks a task as completed.
    /// </summary>
    /// <param name="queuedTask">The task that was completed.</param>
    /// <returns>True if the record was updated.  If a record wasn't modified, something else took it, which is
    /// indicative of an error.</returns>
    private bool CompleteTask(QueuedTask queuedTask) => UpdateTaskStatusAndData(queuedTask, success: true);

    /// <summary>
    /// Removes tasks that are old and no longer relevant.
    /// </summary>
    private void DeleteOldTasks() => _work.DeleteMany(filter: Builders<QueuedTask>.Filter.And(
        Builders<QueuedTask>.Filter.Eq(task => task.Type, TaskData.TaskType.Work),
        Builders<QueuedTask>.Filter.Eq(task => task.Status, QueuedTask.TaskStatus.Succeeded),
        Builders<QueuedTask>.Filter.Lte(task => task.CreatedOn, Timestamp.UnixTimeMs - RETENTION_MS)
    ));

    /// <summary>
    /// Marks a task as completed.
    /// </summary>
    /// <param name="queuedTask">The task that was completed.</param>
    /// <param name="data">The task that was completed.</param>
    /// <returns>True if the record was updated.  If a record wasn't modified, something else took it, which is
    /// indicative of an error.</returns>
    private bool FailTask(QueuedTask queuedTask) => UpdateTaskStatusAndData(queuedTask, success: false);

    /// <summary>
    /// Creates a task that can then be claimed by worker threads.
    /// </summary>
    /// <param name="data">The data needed to perform work on the task.</param>
    /// <param name="track">If true, the config will execute OnTasksCompleted() once all tracked tasks are processed.</param>
    private async void InsertTask(T data = null, bool track = false) => InsertTasks(track, data);

    private async void InsertTasks(bool track = false, params T[] data)
    {
        if (!data.Any())
            return;
        
        QueuedTask[] documents = data
            .Select(d => new QueuedTask
            {
                Data = d,
                Type = TaskData.TaskType.Work,
                Status = QueuedTask.TaskStatus.NotStarted,
                Tracked = track
            })
            .ToArray();
        await _work.InsertManyAsync(documents);

        if (!track)
            return;
        
        // Add the tracked task's ID to the config's waitlist.
        // Set the minimum OnCompleteTime for the next Primary cycle.  
        QueueConfig config = await _config.FindOneAndUpdateAsync<QueueConfig>(
            filter: config => config.OnCompleteTime <= 0,
            update: Builders<QueueConfig>.Update
                .PushEach(config => config.Waitlist, documents.Select(d => d.Id))
                .Set(config => config.OnCompleteTime, Timestamp.UnixTime + (IntervalMs / 1_000)),
            options: new FindOneAndUpdateOptions<QueueConfig>
            {
                ReturnDocument = ReturnDocument.After
            }
        ) ?? await _config.FindOneAndUpdateAsync<QueueConfig>(                                                          // If config is null, the update didn't take
            filter: config => true,                                                                                     // effect; this is because the OnCompleteTime
            update: Builders<QueueConfig>.Update.PushEach(config => config.Waitlist, documents.Select(d => d.Id)),      // was set by a previous task
            options: new FindOneAndUpdateOptions<QueueConfig>
            {
                ReturnDocument = ReturnDocument.After
            }
        );
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
                Builders<QueueConfig>.Filter.Lte(config => config.LastActive, Timestamp.UnixTimeMs - MS_TAKEOVER)
            )
        ),
        update: Builders<QueueConfig>.Update.Combine(
            Builders<QueueConfig>.Update.Set(config => config.PrimaryServiceId, Id),
            Builders<QueueConfig>.Update.Set(config => config.LastActive, Timestamp.UnixTimeMs)
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
    private bool UpdateTaskStatusAndData(QueuedTask queuedTask, bool success)
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
                ? Builders<QueuedTask>.Update.Combine(
                    Builders<QueuedTask>.Update.Set(task => task.Status, QueuedTask.TaskStatus.Succeeded),
                    Builders<QueuedTask>.Update.Set(task => task.Data, queuedTask.Data))
                : Builders<QueuedTask>.Update.Combine(
                    Builders<QueuedTask>.Update.Set(task => task.Status, QueuedTask.TaskStatus.Failed),
                    Builders<QueuedTask>.Update.Set(task => task.Data, queuedTask.Data),
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
                Builders<QueueConfig>.Update.Set(config => config.LastActive, Timestamp.UnixTimeMs),
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

    /// <summary>
    /// Because the QueueService uses private classes and multiple models, the standard platform-common method for detecting
    /// indexing is not available here.  We must override it to provide the information common needs.
    /// </summary>
    /// <param name="type">The data model to get indexes from; this will be the QueueService's task data model.</param>
    /// <returns>An array of properties that may have mongo index attributes attached to them, including for private models
    /// the QueueService needs.</returns>
    internal override PropertyInfo[] GetIndexCandidates(Type type) => base.GetIndexCandidates(type)
        .Union(base.GetIndexCandidates(typeof(QueueConfig)))
        .Union(base.GetIndexCandidates(typeof(QueuedTask)))
        .Union(base.GetIndexCandidates(typeof(TaskData)))
        .Where(info => info.Name != nameof(QueuedTask.Data)) // Must be excluded to prevent recursive index candidate lookups
        .ToArray();

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
        [CompoundIndex(GROUP_KEY_PRIMARY, priority: 2)]
        internal string PrimaryServiceId { get; set; }
        
        [BsonElement(KEY_ACTIVITY)]
        [CompoundIndex(GROUP_KEY_UPDATED, priority: 2)]
        internal long LastActive { get; set; }
        
        [BsonElement(KEY_MINIMUM_WAIT_TIME)]
        [SimpleIndex]
        internal long OnCompleteTime { get; set; }
        
        [BsonElement(KEY_SETTINGS)]
        public RumbleJson Settings { get; set; }
        
        [BsonElement(KEY_WAITLIST)]
        [CompoundIndex(GROUP_KEY_WAITLIST, priority: 1)]
        public IEnumerable<string> Waitlist { get; set; }
        
        [BsonElement(KEY_LAST_TRACK_TIME)]
        [CompoundIndex(GROUP_KEY_WAITLIST, priority: 2)]
        [CompoundIndex(GROUP_KEY_WAITLIST_ELEMENT, priority: 2)]
        [AdditionalIndexKey(GROUP_KEY_WAITLIST_ELEMENT, key: $"{KEY_WAITLIST}.0", priority: 1)]
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
        [CompoundIndex(GROUP_KEY_OWNER, priority: 2)]
        public string ClaimedBy { get; set; }
        
        [BsonElement(KEY_CLAIMED_ON)]
        public long ClaimedOn { get; set; }
        
        [BsonElement(KEY_STATUS)]
        [CompoundIndex(group: GROUP_KEY_FAILURES, priority: 1)]
        [CompoundIndex(GROUP_KEY_STATUS, priority: 1)]
        [CompoundIndex(GROUP_KEY_TRACKING, priority: 1)]
        internal TaskStatus Status { get; set; }
        
        [BsonElement(KEY_TRACKED)]
        [CompoundIndex(GROUP_KEY_TRACKING, priority: 2)]
        internal bool Tracked { get; set; }
        
        [BsonElement(KEY_DATA)]
        internal T Data { get; set; }
        
        [BsonElement(KEY_FAILURES)]
        [CompoundIndex(group: GROUP_KEY_FAILURES, priority: 2)]
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
        protected const string GROUP_KEY_PRIMARY = "type_1_primary_1";
        protected const string GROUP_KEY_UPDATED = "type_1_updated_1";
        protected const string GROUP_KEY_OWNER = "type_1_owner_1";
        protected const string GROUP_KEY_FAILURES = "status_1_failures_1";
        protected const string GROUP_KEY_STATUS = "status_1_type_1_created_1";
        protected const string GROUP_KEY_WAITLIST = "wait_1_lastTrackTime_1";
        protected const string GROUP_KEY_TRACKING = "status_1_tracked_1";
        protected const string GROUP_KEY_WAITLIST_ELEMENT = "wait.0_1_lastTrackTime_1";

        internal const string KEY_CREATED = "created";
        internal const string KEY_TYPE = "type";
    
        [BsonElement(KEY_TYPE)]
        [CompoundIndex(GROUP_KEY_PRIMARY, priority: 1)]
        [CompoundIndex(GROUP_KEY_UPDATED, priority: 1)]
        [CompoundIndex(GROUP_KEY_OWNER, priority: 1)]
        [CompoundIndex(GROUP_KEY_STATUS, priority: 2)]
        [AdditionalIndexKey(GROUP_KEY_STATUS, key: DB_KEY_CREATED_ON, priority: 3)]
        internal TaskType Type { get; set; }

        protected TaskData() => CreatedOn = Timestamp.UnixTimeMs;
    
        internal enum TaskType
        {
            Config = 10,
            Work = 20
        }
    }

#endregion Collection Documents
}  // TODO: add link to health degraded slack message 