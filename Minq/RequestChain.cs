using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Transactions;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Core.Operations;
using RCL.Logging;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Interfaces;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;
using CursorType = MongoDB.Driver.Core.Operations.CursorType;
using ReturnDocument = MongoDB.Driver.Core.Operations.ReturnDocument;

namespace Rumble.Platform.Common.Minq;

public class RequestChain<T> where T : PlatformCollectionDocument
{
    private readonly string EmptyRenderedFilter = Minq<T>.Render(Builders<T>.Filter.Empty);
    internal bool FilterIsEmpty => Minq<T>.Render(_filter) == EmptyRenderedFilter;

    internal string RenderedFilter => Minq<T>.Render(_filter);
    
    internal FilterDefinition<T> _filter { get; set; }
    internal UpdateDefinition<T> _update { get; set; }

    internal bool AbortTransactionOnFailure { get; set; }
    internal Transaction Transaction { get; set; }
    internal bool UsingTransaction => Transaction != null;
    
    private long CacheTimestamp { get; set; }
    private bool Consumed { get; set; }
    private Minq<T> Parent { get; set; }
    private bool UseCache => CacheTimestamp > 0; 

    private int _limit;
    private Dictionary<string, int> _indexWeights;
    private EventHandler<RecordsAffectedArgs> _onRecordsAffected;
    private EventHandler<RecordsAffectedArgs> _onNoneAffected;
    private EventHandler<RecordsAffectedArgs> _onTransactionAborted;
    private IMongoCollection<T> _collection => Parent.Collection;
    private SortDefinition<T> _sort;

    /// <summary>
    /// RequestChains are integral for controlling MINQ's flow.  They're components of a MINQ service and act as the entry
    /// point to both filter and update chains.
    /// </summary>
    /// <param name="parent">The parent Minq object.</param>
    /// <param name="filterChain">An optional FilterChain to begin with.  If unspecified, the filter starts off empty.</param>
    internal RequestChain(Minq<T> parent, FilterChain<T> filterChain = null)
    {
        filterChain ??= new FilterChain<T>().All();
        UpdateIndexWeights(filterChain);
        _filter = filterChain?.Filter ?? Builders<T>.Filter.Empty;
        Parent = parent;
        AbortTransactionOnFailure = true;
    }
    
    /// <summary>
    /// Attempts to complete the RequestChain, disallowing its use again.  Once consumed, the filter is evaluated,
    /// which can result in the automatic creation of indexes if the query isn't covered.
    /// </summary>
    /// <exception cref="PlatformException">Thrown when the chain has already been consumed.</exception>
    private void Consume()
    {
        if (Consumed)
        {
            RequestConsumedException<T> exception = new (this);
            Log.Error(Owner.Default, "MINQ request already consumed; the query will return a default result.", exception: exception);
            throw exception;
        }

        Consumed = true;
        EvaluateFilter();
    }
    

    /// <summary>
    /// If a limit has been specified, this runs the command on Mongo with the limit in place.  If a sort has been specified,
    /// the sort is applied as well.
    /// </summary>
    /// <returns>Mongo's IFindFluent object to be used in future chains.</returns>
    private IFindFluent<T, T> FindWithLimitAndSort() => (_limit switch
    {
        <= 0 when UsingTransaction => _collection.Find(Transaction.Session, _filter),
        <= 0 => _collection.Find(_filter),
        _ when UsingTransaction => _collection.Find(Transaction.Session, _filter).Limit(_limit),
        _ => _collection.Find(_filter).Limit(_limit)
    }).ApplySortDefinition(_sort);

    /// <summary>
    /// Fires off either the RecordsAffected or the NoneAffected event, where appropriate.
    /// </summary>
    /// <param name="affected">The number of records affected by a MINQ.</param>
    private void FireAffectedEvent(long affected)
    {
        // This might look a little ugly, but it's technically more memory efficient since it only
        // allocates memory for the args if event handlers are defined.
        (affected == 0
                ? _onNoneAffected
                : _onRecordsAffected)
            ?.Invoke(this, new RecordsAffectedArgs
            {
                Affected = affected,
                Transaction = Transaction
            });
    }
    
    /// <summary>
    /// Paging allows end users - like players - to get partial results and continue scanning results.  Use paging
    /// if you need to allow an end user to scan the database.  This does not consume the request since it has shared
    /// functionality with Process().
    /// </summary>
    /// <param name="size">The number of results you want back.  For the last page you may get fewer results.</param>
    /// <param name="number">The page number you want to view, zero-indexed.</param>
    /// <param name="remaining">The number of remaining records from the query.  If you have 100 records, skipped 20,
    /// and are viewing 10, this will be 70.</param>
    /// <returns>An array of results.</returns>
    /// <exception cref="PlatformException">Thrown when size is invalid.</exception>
    private T[] PageQuery(int size, int number, out long remaining)
    {
        if (size <= 0)
            throw new PlatformException("Invalid size request.  Size must be greater than zero.", code: ErrorCode.InvalidParameter);

        IFindFluent<T, T> finder = UsingTransaction
            ? _collection
                .Find(Transaction.Session, _filter)
                .Sort(_sort)
                .Skip(Math.Max(0, number * size))
            : _collection
                .Find(_filter)
                .Sort(_sort)
                .Skip(Math.Max(0, number * size));
        remaining = finder.CountDocuments();
        T[] output = finder
            .Limit(size)
            .ToList()
            .ToArray();
        remaining -= output.Length;

        return output;
    }

    /// <summary>
    /// Fires off the OnTransactionAborted event if a transaction was previously consumed and can no longer be used.
    /// </summary>
    /// <param name="methodName">Typically, the name of the calling method.</param>
    /// <returns>True if the transaction cannot be completed.</returns>
    private bool ShouldAbort(string methodName)
    {
        if (!UsingTransaction || !Transaction.Consumed)
            return false;

        _onTransactionAborted?.Invoke(this, new RecordsAffectedArgs
        {
            Affected = 0,
            Transaction = Transaction
        });
        
        Log.Verbose(Owner.Default, $"A transaction was previously aborted; the call to {methodName} can not be completed.");
        return true;
    }

    /// <summary>
    /// The Cache is only usable for read-only requests; this will issue a warning from other terminal methods that are
    /// unsupported.
    /// </summary>
    /// <param name="methodName"></param>
    private void WarnOnUnusedCache(string methodName)
    {
        if (UseCache)
            Log.Warn(Owner.Will, $"MINQ was told to use a cache for its query, but caches aren't available for {methodName}");
    }

    /// <summary>
    /// Issues a WARN-level log when you've specified an event handler that will never fire.  These aren't critical and won't
    /// hurt anything, but it's unnecessary extra code that should be removed.
    /// </summary>
    /// <param name="methodName">Typically, the calling method.  This is used</param>
    private void WarnOnUnusedEvents(string methodName)
    {
        const string message = $"{{0}} will not have any effect on your provided method.  Remove the link in the method chain.";
        object data = new
        {
            Method = methodName
        };
        if (_onNoneAffected != null)
            Log.Warn(Owner.Default, string.Format(message, nameof(OnNoneAffected)), data);
        if (_onRecordsAffected != null)
            Log.Warn(Owner.Default, string.Format(message, nameof(OnRecordsAffected)), data);
        if (_onTransactionAborted != null)
            Log.Warn(Owner.Default, string.Format(message, nameof(OnTransactionAborted)), data);
    }
    
    #region Chainables

    /// <summary>
    /// Used to combine method chains for a request.  Moderately discouraged to use this, as you can accomplish more
    /// readable queries by just using the initial FilterChain instead.
    /// </summary>
    /// <param name="builder">A filter method chain.</param>
    /// <param name="condition">If this is specified, the FilterChain will only be added when it evaluates to true.</param>
    /// <returns>The RequestChain for method chaining.</returns>
    public RequestChain<T> And(Action<FilterChain<T>> builder, bool condition = true)
    {
        if (!condition)
            return this;
        
        FilterChain<T> and = new();
        builder.Invoke(and);
        _filter = Builders<T>.Filter.And(_filter, and.Filter);
        UpdateIndexWeights(and);
        return this;
    }
    
    /// <summary>
    /// Creates a filter that matches all records.  IMPORTANT: this will overwrite any other filters you've built.
    /// </summary>
    /// <returns>The RequestChain for method chaining.</returns>
    public RequestChain<T> All()
    {
        if (_filter != null && _filter != Builders<T>.Filter.Empty)
            Log.Warn(Owner.Default, $"An existing filter is being overwritten by {nameof(All)}(); remove the previous filter definitions");
        _filter = Builders<T>.Filter.Empty;
        return this;
    }
    
    /// <summary>
    /// Instructs MINQ to store the results of the query in a separate collection for a specified duration.  If the
    /// same exact query (as defined by a hash) is run within this duration, the cache collection is used to return old data
    /// instead of executing the query on the main collection again.  This can reduce Mongo's memory and processing usage
    /// while also keeping cached data the same across any number of instances.  Note that this is only valid for read
    /// operations; if you use it in conjunction with writes, you will get a warning log from it.
    /// Cache entries are deleted after they've been expired for one day.
    /// </summary>
    /// <param name="seconds">The duration the cache should be valid for.  Maximum value of 36_000 (ten hours)</param>
    /// <returns>The RequestChain for method chaining.</returns>
    public RequestChain<T> Cache(long seconds)
    {
        seconds = Math.Max(0, Math.Min(36_000, seconds));
        CacheTimestamp = Timestamp.Now + seconds;
        return this;
    }
    
    /// <summary>
    /// Adds a limit to the filter that was built.  Very strongly encouraged in any situation where you expect to
    /// find more than 100 documents to ensure you aren't accidentally polling far too much data.
    /// </summary>
    /// <param name="limit">The maximum number of records to return / modify.</param>
    /// <returns>The RequestChain for method chaining.</returns>
    public RequestChain<T> Limit([Range(0, int.MaxValue)] int limit)
    {
        _limit = limit;
        return this;
    }
    
    /// <summary>
    /// Used to create a negated filter chain.  It's strongly recommended to avoid using this unless necessary, as
    /// you can create easier-to-understand queries with negated equality operators with NotEqualTo.  This is combined with
    /// prior filter chains.
    /// </summary>
    /// <param name="builder">A filter method chain.</param>
    /// <param name="condition">If this is specified, the FilterChain will only be added when it evaluates to true.</param>
    /// <returns>The RequestChain for method chaining.</returns>
    public RequestChain<T> Not(Action<FilterChain<T>> builder, bool condition = true)
    {
        if (!condition)
            return this;
        
        FilterChain<T> not = new();
        builder.Invoke(not);
        
        _filter = Builders<T>.Filter.And(_filter, Builders<T>.Filter.Not(not.Filter));
        UpdateIndexWeights(not);
        return this;
    }
    
    /// <summary>
    /// Defines an action to take when no records were affected.  This will most often be considered a "failed" update.
    /// </summary>
    /// <param name="result">A lambda expression for RecordsAffectedArgs.</param>
    /// <returns>The RequestChain for method chaining.</returns>
    public RequestChain<T> OnNoneAffected(Action<RecordsAffectedArgs> result)
    {
        if (result != null)
            _onNoneAffected += (sender, args) =>
            {
                result.Invoke(args);
            };
        return this;
    }
    
    /// <summary>
    /// Defines an action to take when at least one record was affected.  This will most often be considered a "successful"
    /// update.  It's good practice to add some sanity checks; if you use a bad filter, you might be accidentally impacting
    /// every record on the database instead of the 10 you expected to.  Add some conditionals and logging for safety here
    /// if appropriate.
    /// </summary>
    /// <param name="result">A lambda expression for RecordsAffectedArgs.</param>
    /// <returns>The RequestChain for method chaining.</returns>
    public RequestChain<T> OnRecordsAffected(Action<RecordsAffectedArgs> result)
    {
        if (result != null)
            _onRecordsAffected += (sender, args) =>
            {
                result.Invoke(args);
            };
        return this;
    }
    
    /// <summary>
    /// Used to combine multiple queries.  Slightly discouraged - preferred use is to modify an initial query instead.
    /// For example, if you need to look for a record with Foo.Bar = 5 or model.Foo = 10, use the FilterChain method
    /// ContainedIn(model => model.Foo, new [] { 5, 10 }) instead of two separate filters.  However, if the documents
    /// aren't at all related and you're looking for wildly different fields, the Or filter may use multiple indexes,
    /// so it can still be more performant in some situations - in such cases, use your judgment to decide whether or not
    /// it's more readable to just use a second MINQ chain entirely.
    /// </summary>
    /// <param name="builder">A filter method chain.</param>
    /// <param name="condition">If this is specified, the FilterChain will only be added when it evaluates to true.</param>
    /// <returns>The RequestChain for method chaining.</returns>
    public RequestChain<T> Or(Action<FilterChain<T>> builder, bool condition = true)
    {
        if (!condition)
            return this;
        
        FilterChain<T> or = new();
        builder.Invoke(or);
        _filter = !FilterIsEmpty
            ? Builders<T>.Filter.Or(_filter, or.Filter)
            : or.Filter;
        UpdateIndexWeights(or);
        return this;
    }
    
    /// <summary>
    /// Defines an action to take when a transaction fails.
    /// </summary>
    /// <param name="action">A lambda expression allowing logging events or other diagnostics to include.</param>
    /// <returns>The RequestChain for method chaining.</returns>
    public RequestChain<T> OnTransactionAborted(Action action)
    {
        if (action != null)
            _onTransactionAborted += (sender, args) => action.Invoke();
        return this;
    }
    
    /// <summary>
    /// Defines a sort for the query.  Note that sorts, like filters, rely on indexes.  Keep this in mind if a query
    /// begins misbehaving and MINQ hasn't automatically created an index for it.
    /// </summary>
    /// <param name="sort">A sort method chain.</param>
    /// <returns>The RequestChain for method chaining.</returns>
    public RequestChain<T> Sort(Action<SortChain<T>> sort)
    {
        if (_sort != null)
            Log.Warn(Owner.Default, $"Only one call to MINQ {nameof(Sort)} can be honored per request.  Combine them into one call.");
        SortChain<T> chain = new ();
        sort.Invoke(chain);

        _sort = chain.Sort;
        return this;
    }
    
    /// <summary>
    /// Creates a filter to limit the data that is returned or affected in Mongo.
    /// </summary>
    /// <param name="query">A lambda expression that builds your filter chain.  The entire filter should be built
    /// with a method chain here.</param>
    /// <returns>A RequestChain object, which allows you to issue terminal commands such as Update() or ToList().</returns>
    public RequestChain<T> Where(Action<FilterChain<T>> query)
    {
        FilterChain<T> filter = new();
        query.Invoke(filter);
        
        WarnOnFilterOverwrite(nameof(Where));

        _filter = filter.Filter;
        UpdateIndexWeights(filter);
        return this;
    }
    
    public RequestChain<T> ExactId(string id) => Where(query => query.EqualTo(doc => doc.Id, id));
    #endregion Chainables

    private void WarnOnFilterOverwrite(string method)
    {
        if (_filter != null && Minq<T>.Render(_filter) != EmptyRenderedFilter)
            Log.Warn(Owner.Default, $"Filter was not empty when {method}() was called.  {method}() overrides previous filters.  Is this intentional?");
    }
    
    #region Index Management
    /// <summary>
    /// Runs automatic analysis on the filter the chain created.  If the filter is not covered by an index, this will
    /// try to create an appropriate one for it.
    /// </summary>
    private void EvaluateFilter()
    {
        MinqIndex[] existing = Parent.RefreshIndexes(out int next);
        MinqIndex suggested = new(_indexWeights);

        if (suggested.Fields.Count == 0)
            return;

        if (suggested.IsProbablyCoveredBy(existing))
            return;
        
        bool deliberateEmptyFilter = Minq<T>.Render(_filter) == Minq<T>.Render(Builders<T>.Filter.Empty);
        if (deliberateEmptyFilter)
            return;

        Explain(out MongoQueryStats stats);

        if (stats == null)
        {
            Log.Warn(Owner.Will, "MongoQueryStats was null; investigation needed, auto-indexing disabled for this query.");
            return;
        }

        try
        {
            if (stats.IsFullyCovered)
                return;

            if (stats.IsPartiallyCovered)
                Log.Info(Owner.Will, "A MINQ query was partially covered by existing indexes; a new index will be added");
            else if (stats.IsNotCovered)
                Log.Warn(Owner.Will, "A MINQ query is not covered by any index; a new index will be added");
            
            suggested.Name = $"{MinqIndex.INDEX_PREFIX}{next}";
            CreateIndexModel<BsonDocument> model = suggested.GenerateIndexModel();
            Parent.GenericCollection.Indexes.CreateOne(model);


            Minq<T>.TryRender(_filter, out RumbleJson filterJson, out string filterString);
            
            Log.Info(Owner.Will, "MINQ automatically created an index", data: new
            {
                Collection = _collection.CollectionNamespace.CollectionName,
                IndexName = suggested.Name,
                Filter = filterJson,
                FilterAsString = filterString
            });
        }
        catch (Exception e)
        {
            Log.Error(Owner.Will, stats.DocumentsReturned > 1_000 
                ? "Unable to create automatic MINQ index, and the query scanned many documents; investigation required" 
                : "Unable to create automatic MINQ index; investigation possibly needed",
                data: new
                {
                    Filter = (RumbleJson)Minq<T>.Render(_filter),
                    Stats = stats
                }, exception: e
            );
        }
    }
    
    /// <summary>
    /// Runs an "explain" command on Mongo and parses the result into a usable C# data structure.  This is an important
    /// factor in keeping an eye on our DB performance.
    /// </summary>
    /// <param name="stats">An object containing information about keys/records scanned and whether or not the query was covered by indexes.</param>
    private void Explain(out MongoQueryStats stats)
    {
        stats = null;
        
        try
        {
            // Unfortunately this is unable to leverage Minq<T>.Render(filter), because it needs to be a BSON document,
            // not a string.
            BsonDocument command = new()
            {
                { "explain", new BsonDocument
                {
                    { "find", _collection.CollectionNamespace.CollectionName }, 
                    { "filter", _filter.Render(
                        documentSerializer: BsonSerializer.SerializerRegistry.GetSerializer<T>(),
                        serializerRegistry: BsonSerializer.SerializerRegistry
                    ).AsBsonDocument }
                } }
            };
            stats = new MongoQueryStats(_collection.Database.RunCommand<BsonDocument>(command));

        }
        catch (Exception e)
        {
            Log.Error(Owner.Will, "Unable to parse Mongo explanation", exception: e);
        }
    }
    
    /// <summary>
    /// Updates the index weights.  Index weights are used to automatically detect the order an auto-index should be
    /// created in.  Fields that are used more frequently will see higher weights.  WIP.
    /// </summary>
    /// <param name="filter">The partial FilterChain to update the weights with.</param>
    private void UpdateIndexWeights(FilterChain<T> filter)
    {
        if (filter == null)
            return;
        _indexWeights ??= new Dictionary<string, int>();
        foreach (KeyValuePair<string, int> pair in filter.IndexWeights.Where(pair => !_indexWeights.TryAdd(pair.Key, pair.Value)))
            _indexWeights[pair.Key] += pair.Value;
    }
    #endregion Index Management
    
    #region Terminal Commands
    /// <summary>
    /// Returns the number of documents using the filter built from method-chaining.
    /// </summary>
    /// <returns>The number of documents matching the filter.</returns>
    public long Count()
    {
        WarnOnUnusedCache(nameof(Count));
        WarnOnUnusedEvents(nameof(Count));
        Consume();
        
        try
        {
            return _collection.CountDocuments(_filter);
        }
        catch
        {
            Transaction?.TryAbort();
            return 0;
        }
    }
    
    /// <summary>
    /// Deletes everything matching the current filter.
    /// </summary>
    /// <returns>The number of affected records.</returns>
    public long Delete()
    {
        WarnOnUnusedCache(nameof(Delete));
        Consume();
        if (ShouldAbort(nameof(Delete)))
            return 0;
        
        Consumed = true;

        try
        {
            long output = (UsingTransaction
                ? _collection.DeleteMany(Transaction.Session, _filter)
                : _collection.DeleteMany(_filter)).DeletedCount;
        
            FireAffectedEvent(output);
            return output;
        }
        catch (Exception e)
        {
            if (e is RequestConsumedException<T> consumed)
                Log.Error(Owner.Default, "Request has already been consumed; MINQ will return a default value.", exception: consumed);
            Transaction?.TryAbort();
            return 0;
        }
    }

    /// <summary>
    /// Alias for ToList() with a limit of 1.  Throws an exception if no records are found.
    /// </summary>
    /// <returns>The first model matching the specified query.</returns>
    /// <exception cref="IndexOutOfRangeException">Thrown when no records are found.</exception>
    public T First() => FirstOrDefault() ?? throw new PlatformException("No models found.", code: ErrorCode.MongoRecordNotFound);

    public T FirstOrDefault()
    {
        _limit = 1;

        try
        {
            return ToList().FirstOrDefault();
        }
        catch
        {
            Transaction?.TryAbort();
            return null;
        }
    }
    
    /// <summary>
    /// Adds one or more records to the database.  Note that filters are completely ignored in this method.  It exists
    /// as part of a chain in case you need to use a transaction.  If you're not using a transaction, it's recommended
    /// to use mongo.Insert() instead.  Null objects are ignored.
    /// </summary>
    /// <param name="models">The objects you want to insert, of type PlatformCollectionDocument</param>
    /// <exception cref="PlatformException">If there are no valid objects to insert, a PlatformException will be thrown.</exception>
    public void Insert(params T[] models)
    {
        WarnOnUnusedCache(nameof(Insert));
        Consume();

        if (ShouldAbort(nameof(Insert)))
            return;

        T[] toInsert = models.Where(model => model != null).ToArray();
        toInsert = toInsert.Select(insert =>
        {
            insert.CreatedOn = Timestamp.Now;
            return insert;
        }).ToArray();
        
        if (!toInsert.Any())
            throw new PlatformException("You must provide at least one model to insert.  Null objects are ignored.");
        try
        {
            if (UsingTransaction)
                _collection.InsertMany(Transaction.Session, toInsert);
            else
                _collection.InsertMany(toInsert);
            FireAffectedEvent(models.Length);
        }
        catch (MongoBulkWriteException<T> e)
        {
            Transaction?.TryAbort();
            if (e.WriteErrors.Any(error => error.Category == ServerErrorCategory.DuplicateKey))
                throw new UniqueConstraintException<T>(null, e);
            throw;
        }
        catch (MongoCommandException e)
        {
            Transaction?.TryAbort();
            if (e.CodeName == ServerErrorCategory.DuplicateKey.GetDisplayName())
                throw new UniqueConstraintException<T>(null, e);
            throw;
        }
        catch
        {
            Transaction?.TryAbort();
        }
    }
    
    /// <summary>
    /// Paging allows end users - like players - to get partial results and continue scanning results.  Use paging
    /// if you need to allow an end user to scan the database.  This consumes the request.
    /// </summary>
    /// <param name="size">The number of results you want back.  For the last page you may get fewer results.</param>
    /// <param name="number">The page number you want to view, zero-indexed.</param>
    /// <param name="remaining">The number of remaining records from the query.  If you have 100 records, skipped 20,
    /// and are viewing 10, this will be 70.</param>
    /// <returns>An array of results.</returns>
    /// <exception cref="PlatformException">Thrown when size is invalid.</exception>
    public T[] Page(int size, int number, out long remaining)
    {
        Consume();
        WarnOnUnusedEvents(nameof(Page));

        if (size <= 0)
            throw new PlatformException("Invalid size request.  Size must be greater than zero.", code: ErrorCode.InvalidParameter);

        return PageQuery(size, number, out remaining);
    }
    
    /// <summary>
    /// Paging allows end users - like players - to get partial results and continue scanning results.  Use paging
    /// if you need to allow an end user to scan the database.  This consumes the request.
    /// </summary>
    /// <param name="size">The number of results you want back.  For the last page you may get fewer results.</param>
    /// <param name="number">The page number you want to view, zero-indexed.</param>
    /// <param name="remaining">The number of remaining records from the query.  If you have 100 records, skipped 20,
    /// and are viewing 10, this will be 70.</param>
    /// <param name="total">The total number of documents from the query.</param>
    /// <returns>An array of results.</returns>
    /// <exception cref="PlatformException">Thrown when size is invalid.</exception>
    public T[] Page(int size, int number, out long remaining, out long total)
    {
        Consume();
        WarnOnUnusedEvents(nameof(Page));

        if (size <= 0)
            throw new PlatformException("Invalid size request.  Size must be greater than zero.", code: ErrorCode.InvalidParameter);

        T[] output = PageQuery(size, number, out remaining);

        // Accommodate edge case when viewing the last page
        total = remaining > 0
            ? remaining + size * number
            : (number - 1) * size + output.Length;

        return output;
    }
    
    /// <summary>
    /// Use this method to perform operations on a data set.  This is primarily for transformations; such as writing
    /// import / export / upgrade scripts, and is generally discouraged for use within core service code.  It is expensive
    /// in the sense that the typical use case will be performing operations on large data sets, so the service will be loading
    /// significant counts of models into memory.
    /// </summary>
    /// <param name="batchSize">The number of records to batch at one time.</param>
    /// <param name="onBatch">The action to take on the BatchData on every batch.</param>
    public void Process(int batchSize, Action<BatchData<T>> onBatch)
    {
        Consume();
        
        try
        {
            long start = TimestampMs.Now;
            int page = 0;
            
            BatchData<T> args;
            do
            {
                T[] results = PageQuery(batchSize, page, out long remaining);
                args = new BatchData<T>
                {
                    Results = results,
                    Remaining = remaining,
                    OperationRuntime = Timestamp.Now - start,
                    Processed = page * batchSize,
                    Total = page * batchSize + remaining + results.Length,
                    Continue = remaining > 0
                };
                onBatch?.Invoke(args);
                page++;
            } while (args.Continue);
    
            long timeTaken = TimestampMs.Now - start;
            
            if (UsingTransaction && timeTaken > 20_000)
                Log.Warn(Owner.Default, $"{nameof(Process)} took a while to execute and is using a transaction; this may fail if over 30s", data: new
                {
                    TimeTakenMs = timeTaken
                });
        }
        catch
        {
            Transaction?.TryAbort();
        }
    }
    
    /// <summary>
    /// Pulls nested data out of mongo from your model.  If you don't need the full document for your query, you can
    /// reduce your memory footprint by only pulling out the data that you need.
    /// </summary>
    /// <param name="expression">A lambda expression selecting the nested data you want to retrieve, e.g. model => model.NestedObject</param>
    /// <typeparam name="U">The data type of the nested object.</typeparam>
    /// <returns>An array </returns>
    public U[] Project<U>(Expression<Func<T, U>> expression)
    {
        WarnOnUnusedCache(nameof(Project));
        WarnOnUnusedEvents(nameof(Project));
        Consume();
        
        try
        {
            // PLATF-6498: Rather than use actual Mongo Projection, we have to load all the results in memory and use
            // LINQ to make the conversion.  This is a problem at the driver level.  In Mongo's driver 2.13.0, the
            // Mongo projection worked just fine, but after we made the critical security upgrade to 2.20.0, it began
            // throwing exceptions.
            // This should be addressed at a future date.
            return FindWithLimitAndSort()
                .ToList()
                .Select(expression.Compile())
                .ToArray();
        }
        catch (Exception e)
        {
            Log.Local(Owner.Will, e.Message, emphasis: Log.LogType.ERROR);
            Transaction?.TryAbort();
            return Array.Empty<U>();
        }
    }
    
    /// <summary>
    /// Returns the results of your MINQ pipeline as an Array.  This requires an additional data transformation of
    /// List to Array.
    /// </summary>
    /// <returns>An array of matching models in the database.</returns>
    public T[] ToArray()
    {
        try
        {
            T[] output = ToList().ToArray();
            return output;
        }
        catch
        {
            Transaction?.TryAbort();
            return Array.Empty<T>();
        }
    }
    
    /// <summary>
    /// Returns the results of your MINQ pipeline as a List.
    /// </summary>
    /// <returns>A list of matching models in the database.</returns>
    public List<T> ToList()
    {
        WarnOnUnusedEvents(nameof(ToList));
        Consume();

        try
        {
            if (!UseCache)
                return FindWithLimitAndSort().ToList();
    
            if (Parent.CheckCache(Minq<T>.Render(_filter), out T[] data))
                return data.ToList();
    
            List<T> output = FindWithLimitAndSort().ToList();
            Parent.Cache(_filter, output.ToArray(), CacheTimestamp, Transaction);
            // Mark each document with the cache's expiration.
            foreach (T document in output)
                document.CachedUntil = CacheTimestamp;
            return output;
        }
        catch
        {
            Transaction?.TryAbort();
            return new List<T>();
        }
    }
    
    /// <summary>
    /// Performs an update on all documents matching your specified filter.  Note that you can't set the same field twice
    /// with the same chain; for non-primitive types like arrays this will throw an exception.
    /// </summary>
    /// <param name="update">A lambda expression for an update chain builder.  Set all of your fields in one chain with it.</param>
    /// <returns>The number of records affected by the update.</returns>
    /// <exception cref="PlatformException">Thrown when there's a write conflict from updating a field more than once.</exception>
    /// <exception cref="MongoWriteException">Thrown when there's an unknown problem with the update.</exception>
    public long Update(Action<UpdateChain<T>> update)
    {
        WarnOnUnusedCache(nameof(Update));
        Consume();

        if (ShouldAbort(nameof(Update)))
            return 0;

        UpdateChain<T> updateChain = new();
        update.Invoke(updateChain);
        _update = updateChain.Update;
        
        try
        {
            if (_limit > 0)
                Log.Local(Owner.Default, "Update called with a Limit; Limit is unsupported by the Mongo driver and may be unintentional.");
            
            long output = (UsingTransaction
                ? _collection.UpdateMany(Transaction.Session, _filter, _update)
                : _collection.UpdateMany(_filter, _update)).ModifiedCount;
            
            FireAffectedEvent(output);
            
            return output;
        }
        catch (MongoBulkWriteException<T> e)
        {
            Transaction?.TryAbort();
            if (e.WriteErrors.Any(error => error.Category == ServerErrorCategory.DuplicateKey))
                throw new UniqueConstraintException<T>(null, e);
            throw;
        }
        catch (MongoCommandException e)
        {
            Transaction?.TryAbort();
            if (e.CodeName == ServerErrorCategory.DuplicateKey.GetDisplayName())
                throw new UniqueConstraintException<T>(null, e);
            throw;
        }
        catch (MongoWriteException)
        {
            Transaction?.TryAbort();
            return 0;
        }
    }

    public T UpdateAndReturnOne(Action<UpdateChain<T>> update)
    {
        WarnOnUnusedCache(nameof(UpdateAndReturnOne));
        Consume();

        if (ShouldAbort(nameof(Update)))
            return null;

        UpdateChain<T> updateChain = new();
        update.Invoke(updateChain);
        _update = updateChain.Update;

        try
        {
            FindOneAndUpdateOptions<T> options = new()
            {
                IsUpsert = false,
                ReturnDocument = MongoDB.Driver.ReturnDocument.After
            };
            
            T output = UsingTransaction
                ? _collection.FindOneAndUpdate(Transaction.Session, _filter, _update, options)
                : _collection.FindOneAndUpdate(_filter, _update, options);
            
            if (output != null)
                FireAffectedEvent(1);
            
            return output;
        }
        catch (MongoBulkWriteException<T> e)
        {
            Transaction?.TryAbort();
            if (e.WriteErrors.Any(error => error.Category == ServerErrorCategory.DuplicateKey))
                throw new UniqueConstraintException<T>(null, e);
            throw;
        }
        catch (MongoCommandException e)
        {
            Transaction?.TryAbort();
            if (e.CodeName == ServerErrorCategory.DuplicateKey.GetDisplayName())
                throw new UniqueConstraintException<T>(null, e);
            throw;
        }
        catch (MongoException)
        {
            Transaction?.TryAbort();
            return null;
        }
    }

    public long UpdateRelative(Expression<Func<T, long>> field, long value)
    {
        MergeStageOptions<T> options = new ()
        {
            WhenMatched = MergeStageWhenMatched.Merge,
            WhenNotMatched = MergeStageWhenNotMatched.Discard
        };
        // PipelineStageDefinition<T, T>[] pipeline = new[]
        // {
        //     PipelineStageDefinitionBuilder.Match(_filter),
        //     new BsonDocument("{}"),
        //     PipelineStageDefinitionBuilder.Merge<T>(_collection.CollectionNamespace.CollectionName, options)
        //     
        // };

        string renderedField = Minq<T>.Render(field);
        _collection
            .Aggregate()
            .Match(_filter)
            .AppendStage<T>($"{{ $addFields: {{ {renderedField}: {{ $add: ['${renderedField}', '{value}'] }} }} }}")
            .Merge(_collection, mergeOptions: options);
        
        return 0;
    }

    /// <summary>
    /// Performs an update on all documents matching your specified filter, then returns the modified documents.  Note
    /// that you can't set the same field twice with the same chain; for non-primitive types like arrays this will throw an exception.
    /// IMPORTANT: There is a platform-common-enforced hard cap of 10k documents when using this.  If more than 10k
    /// documents are found, this will throw an exception.  This is a performance limitation, as returning >10k documents
    /// can easily overwhelm consuming services.
    /// </summary>
    /// <param name="update">A lambda expression for an update chain builder.  Set all of your fields in one chain with it.</param>
    /// <param name="affected">The number of records affected by the update.</param>
    /// <returns>The number of records affected by the update.</returns>
    /// <exception cref="PlatformException">Thrown when there's a write conflict from updating a field more than once.</exception>
    /// <exception cref="MongoWriteException">Thrown when there's an unknown problem with the update.</exception>
    public T[] UpdateAndReturn(Action<UpdateChain<T>> update, out long affected)
    {
        const int MAX_LIMIT = 10_000;
        WarnOnUnusedCache(nameof(UpdateAndReturn));
        Consume();
        
        if (_limit == 0)
            Log.Warn(Owner.Will, $"Use {nameof(Limit)}() to avoid a potential performance issue with {nameof(UpdateAndReturn)}() with large data sets.", data: new
            {
                maxLimit = MAX_LIMIT
            });

        affected = 0;

        if (ShouldAbort(nameof(UpdateAndReturn)))
            return Array.Empty<T>();
        
        string[] ids = FindWithLimitAndSort()
            .Project(Builders<T>.Projection.Expression(model => model.Id))
            .ToList()
            .ToArray();

        if (ids.Length > MAX_LIMIT)
            throw new PlatformException($"More documents found than are supported by {nameof(UpdateAndReturn)}.  Limit your query, or use {nameof(Update)}.");
        
        UpdateChain<T> updateChain = new UpdateChain<T>();
        update.Invoke(updateChain);
        _update = updateChain.Update;

        try
        {
            FilterDefinition<T> filter = Builders<T>.Filter.In(document => document.Id, ids);
            affected = (UsingTransaction
                ? _collection.UpdateMany(Transaction.Session, filter, _update)
                : _collection.UpdateMany(filter, _update)).ModifiedCount;

            return _collection
                .Find(filter)
                .ToList()
                .ToArray();
        }
        catch (MongoBulkWriteException<T> e)
        {
            Transaction?.TryAbort();
            if (Transaction != null)
                affected = 0;
            if (e.WriteErrors.Any(error => error.Category == ServerErrorCategory.DuplicateKey))
                throw new UniqueConstraintException<T>(null, e);
            throw;
        }
        catch (MongoCommandException e)
        {
            Transaction?.TryAbort();
            if (Transaction != null)
                affected = 0;
            if (e.CodeName == ServerErrorCategory.DuplicateKey.GetDisplayName())
                throw new UniqueConstraintException<T>(null, e);
            throw;
        }
        catch (MongoWriteException)
        {
            Transaction?.TryAbort();
            if (Transaction != null)
                affected = 0;
            return Array.Empty<T>();
        }
        catch (MongoException)
        {
            Transaction?.TryAbort();
            if (Transaction != null)
                affected = 0;
            return Array.Empty<T>();
        }
    }

    /// <summary>
    /// Performs an update on all documents matching your specified filter, then returns the modified documents.  Note
    /// that you can't set the same field twice with the same chain; for non-primitive types like arrays this will throw an exception.
    /// IMPORTANT: There is a platform-common-enforced hard cap of 10k documents when using this.  If more than 10k
    /// documents are found, this will throw an exception.  This is a performance limitation, as returning >10k documents
    /// can easily overwhelm consuming services.
    /// </summary>
    /// <param name="update">A lambda expression for an update chain builder.  Set all of your fields in one chain with it.</param>
    /// <returns>The number of records affected by the update.</returns>
    /// <exception cref="PlatformException">Thrown when there's a write conflict from updating a field more than once.</exception>
    /// <exception cref="MongoWriteException">Thrown when there's an unknown problem with the update.</exception>
    public T[] UpdateAndReturn(Action<UpdateChain<T>> update) => UpdateAndReturn(update, out _);
    
    
    /// <summary>
    /// Attempts to update or create a single record on the database.  If the record does not exist, one will not be created.
    /// This is effectively the same command as "ReplaceOne" from the stock Mongo driver.
    /// </summary>
    /// <param name="model">A model to update on the database.</param>
    /// <returns>The same model you passed into it.  This is for consistency with the other overload of Upsert.</returns>
    /// <exception cref="PlatformException">Thrown if for some reason the database could not insert the document; likely a unique constraint violation.</exception>
    public T Update(T model)
    {
        WarnOnUnusedCache(nameof(Count));
        Consume();

        if (ShouldAbort(nameof(Update)))
            return default;
        
        WarnOnFilterOverwrite(nameof(Update));
        
        _filter = model.Id == null
            ? Builders<T>.Filter.Empty
            : Builders<T>.Filter.Eq(t => t.Id, model.Id);

        ReplaceOptions options = new ReplaceOptions
        {
            IsUpsert = false
        };

        try
        {
            ReplaceOneResult result = UsingTransaction
                ? _collection.ReplaceOne(Transaction.Session, _filter, model, options)
                : _collection.ReplaceOne(_filter, model, options);
            
            FireAffectedEvent(result.ModifiedCount);

            return model;
        }
        catch (MongoBulkWriteException<T> e)
        {
            Transaction?.TryAbort();
            if (e.WriteErrors.Any(error => error.Category == ServerErrorCategory.DuplicateKey))
                throw new UniqueConstraintException<T>(null, e);
            throw;
        }
        catch (MongoCommandException e)
        {
            Transaction?.TryAbort();
            if (e.CodeName == ServerErrorCategory.DuplicateKey.GetDisplayName())
                throw new UniqueConstraintException<T>(null, e);
            throw;
        }
        catch
        {
            Transaction?.TryAbort();
            return null;
        }
    }
    
    /// <summary>
    /// Attempts to update a single record that matches your filter.  If no record was affected, one will be created, matching
    /// the specified update and filter.
    /// </summary>
    /// <param name="update">A lambda expression for an update chain builder.</param>
    /// <returns>The model that was updated or inserted (post-update).</returns>
    /// <exception cref="PlatformException">Thrown when there's a write conflict from updating a field more than once.</exception>
    /// <exception cref="MongoWriteException">Thrown when there's an unknown problem with the update.</exception>
    public long UpsertMany(Action<UpdateChain<T>> update = null)
    {
        WarnOnUnusedCache(nameof(Count));
        Consume();

        if (ShouldAbort(nameof(Upsert)))
            return default;

        UpdateChain<T> updateChain = new();
        update?.Invoke(updateChain);
        _update = updateChain.Update;

        UpdateOptions options = new()
        {
            IsUpsert = true
        };

        try
        {
            long output = UsingTransaction
                ? _collection.UpdateMany(Transaction.Session, _filter, _update, options).ModifiedCount
                : _collection.UpdateMany(_filter, _update, options).ModifiedCount;

            FireAffectedEvent(output);

            return output;
        }
        catch (MongoBulkWriteException<T> e)
        {
            
            Transaction?.TryAbort();
            if (e.WriteErrors.Any(error => error.Category == ServerErrorCategory.DuplicateKey))
                throw new UniqueConstraintException<T>(null, e);
            throw;
        }
        catch (MongoCommandException e)
        {
            Transaction?.TryAbort();
            if (e.CodeName == ServerErrorCategory.DuplicateKey.GetDisplayName())
                throw new UniqueConstraintException<T>(null, e);
            throw;
        }
        catch (Exception e)
        {
            Log.Error(Owner.Default, "Unable to UpsertMany due to an exception", exception: e);
            Transaction?.TryAbort();
            return 0;
        }
    }

    /// <summary>
    /// Attempts to update a single record that matches your filter.  If no record was affected, one will be created, matching
    /// the specified update and filter.
    /// </summary>
    /// <param name="query">A lambda expression for an update chain builder.</param>
    /// <returns>The model that was updated or inserted (post-update).</returns>
    /// <exception cref="PlatformException">Thrown when there's a write conflict from updating a field more than once.</exception>
    /// <exception cref="MongoWriteException">Thrown when there's an unknown problem with the update.</exception>
    public T Upsert(Action<UpdateChain<T>> update = null)
    {
        WarnOnUnusedCache(nameof(Count));
        Consume();

        if (ShouldAbort(nameof(Upsert)))
            return default;

        UpdateChain<T> updateChain = new();
        update?.Invoke(updateChain);
        updateChain.SetOnInsert(doc => doc.CreatedOn, Timestamp.Now);
        // updateChain.SetOnInsert(doc => doc.CreatedOn, Timestamp.UnixTime);
        _update = updateChain.Update;
        

        FindOneAndUpdateOptions<T> options = new()
        {
            IsUpsert = true,
            ReturnDocument = MongoDB.Driver.ReturnDocument.After
        };

        try
        {
            T output = UsingTransaction
                ? _collection.FindOneAndUpdate(Transaction.Session, _filter, _update, options)
                : _collection.FindOneAndUpdate(_filter, _update, options);

            FireAffectedEvent(1);

            return output;
        }
        catch (MongoBulkWriteException<T> e)
        {
            Transaction?.TryAbort();
            if (e.WriteErrors.Any(error => error.Category == ServerErrorCategory.DuplicateKey))
                throw new UniqueConstraintException<T>(null, e);
            throw;
        }
        catch (MongoCommandException e)
        {
            Transaction?.TryAbort();
            if (e.CodeName == ServerErrorCategory.DuplicateKey.GetDisplayName())
                throw new UniqueConstraintException<T>(null, e);
            throw;
        }
        catch (Exception e)
        {
            Log.Error(Owner.Default, "Unable to Upsert due to an exception", exception: e);
            Transaction?.TryAbort();
            return null;
        }
    }
    
    /// <summary>
    /// Attempts to update or create a single record on the database.  If an existing record was not modified, one will be created.
    /// This is effectively the same command as "ReplaceOne" from the stock Mongo driver.
    /// </summary>
    /// <param name="model">A model to update on the database.</param>
    /// <returns>The same model you passed into it.  This is for consistency with the other overload of Upsert.</returns>
    /// <exception cref="PlatformException">Thrown if for some reason the database could not insert the document; likely a unique constraint violation.</exception>
    public T Upsert(T model)
    {
        WarnOnUnusedCache(nameof(Count));
        Consume();

        if (ShouldAbort(nameof(Upsert)))
            return default;
        
        WarnOnFilterOverwrite(nameof(Upsert));
        
        _filter = model.Id == null
            ? Builders<T>.Filter.Empty
            : Builders<T>.Filter.Eq(t => t.Id, model.Id);

        ReplaceOptions options = new()
        {
            IsUpsert = true
        };

        try
        {
            ReplaceOneResult result = UsingTransaction
                ? _collection.ReplaceOne(Transaction.Session, _filter, model, options)
                : _collection.ReplaceOne(_filter, model, options);

            FireAffectedEvent(result.ModifiedCount);

            return model;
        }
        catch (MongoBulkWriteException<T> e)
        {
            Transaction?.TryAbort();
            if (e.WriteErrors.Any(error => error.Category == ServerErrorCategory.DuplicateKey))
                throw new UniqueConstraintException<T>(null, e);
            throw;
        }
        catch (MongoCommandException e)
        {
            Transaction?.TryAbort();
            if (e.CodeName == ServerErrorCategory.DuplicateKey.GetDisplayName())
                throw new UniqueConstraintException<T>(null, e);
            throw;
        }
        catch
        {
            Transaction?.TryAbort();
            return null;
        }
    }
    
    /// <summary>
    /// Searches specified fields of your model.  Your model must implement ISearchable to work with Search.
    /// There are some limitations because searches are relatively expensive compared to other queries.  For its first iteration,
    /// there's a maximum term limit of 5, terms must be of length 3+, no more than 10 fields can be used, and the returned
    /// document limit is capped at 1000.
    /// </summary>
    /// <param name="terms">The terms to search for.  Terms must be non-null, non-whitespace.  These are naive regex with no fuzzy distance logic (Levenshtein Distance).  Mongo does support it though MINQ will not, but has many limitations; see SEARCH.md for more information.</param>
    /// <returns>An array of models matching your search.</returns>
    public T[] Search(string[] terms)
    {
        if (ShouldAbort(nameof(Search)))
            return default;
        
        T searchModel = (T)Activator.CreateInstance(typeof(T));
        if (searchModel is not ISearchable<T> searchable)
        {
            Log.Error(Owner.Default, $"In order to use {nameof(Minq<T>)}.{nameof(Search)}, your model must implement {nameof(ISearchable<T>)}.");
            return Array.Empty<T>();
        }

        Dictionary<Expression<Func<T, object>>, int> weights = searchable.DefineSearchWeights();
        Expression<Func<T, object>>[] fields = weights.Keys.ToArray();
        
        if (typeof(T).IsAssignableFrom(typeof(ISearchable<T>)))
        {
            Log.Error(Owner.Default, $"In order to use {nameof(Minq<T>)}.{nameof(Search)}, your model must implement {nameof(ISearchable<T>)}.");
            return Array.Empty<T>();
        }

        if (terms.Length > ISearchable<T>.MAXIMUM_TERMS)
            Log.Local(Owner.Default, $"Too many search terms provided; only {ISearchable<T>.MAXIMUM_TERMS} are used.", emphasis: Log.LogType.WARN);
        if (fields.Length > ISearchable<T>.MAXIMUM_FIELDS)
        {
            Log.Error(Owner.Default, $"Too many search fields provided; search will return no results.", data: new
            {
                FieldCount = fields.Length,
                AllowedCount = ISearchable<T>.MAXIMUM_FIELDS
            });
            return Array.Empty<T>();
        }

        if (_limit == default)
            _limit = 25;
        else if (_limit > ISearchable<T>.MAXIMUM_LIMIT)
        {
            Log.Warn(Owner.Default, "Search result limit exceeded and has been lowered by common.  Searches are expensive; lower your limit.");
            _limit = ISearchable<T>.MAXIMUM_LIMIT;
        }

        ISearchable<T>.SanitizeTerms(terms, out string[] sanitizedTerms);

        if (!sanitizedTerms.Any())
        {
            Log.Warn(Owner.Default, $"No valid search terms detected; {nameof(Search)}() will return an empty array.");
            return Array.Empty<T>();
        }
        
        T[] output = And(and => and.Or(or =>
            {
                foreach (string term in sanitizedTerms)
                    foreach (Expression<Func<T, object>> field in fields)
                        or.ContainsSubstring(field, term);
            }))
            .ToArray();
        
        ISearchable<T>.WeighSearchResults(weights, terms, (ISearchable<T>[])output);

        return output
            .OrderByDescending(model => ((ISearchable<T>)model).SearchWeight)
            .ToArray();
    }
    #endregion Terminal Commands
}