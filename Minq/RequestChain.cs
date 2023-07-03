using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Linq.Expressions;
using System.Transactions;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Core.Operations;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;
using CursorType = MongoDB.Driver.Core.Operations.CursorType;
using ReturnDocument = MongoDB.Driver.Core.Operations.ReturnDocument;

namespace Rumble.Platform.Common.Minq;

public class RequestChain<T> where T : PlatformCollectionDocument
{
    private Dictionary<string, int> _indexWeights { get; set; }
    private IMongoCollection<T> _collection => Parent.Collection;
    internal FilterDefinition<T> _filter { get; set; }
    internal UpdateDefinition<T> _update { get; set; }
    private SortDefinition<T> _sort { get; set; }
    
    private Minq<T> Parent { get; set; }
    private int _limit { get; set; }
    private bool Consumed { get; set; }
    private bool UseCache => CacheTimestamp > 0; 
    private long CacheTimestamp { get; set; }

    private BsonDocument RenderedFilter => _filter.Render(BsonSerializer.SerializerRegistry.GetSerializer<T>(), BsonSerializer.SerializerRegistry);
    
    internal Transaction Transaction { get; set; }
    internal bool UsingTransaction => Transaction != null;
    
    private EventHandler<RecordsAffectedArgs> _onRecordsAffected;
    private EventHandler<RecordsAffectedArgs> _onNoneAffected;
    private EventHandler<RecordsAffectedArgs> _onTransactionAborted;

    private string FilterAsString => ConvertFilterToString(_filter);
    
    private string UpdateAsString => _update.Render(
        BsonSerializer.SerializerRegistry.GetSerializer<T>(),
        BsonSerializer.SerializerRegistry
    ).AsBsonDocument.ToString();

    internal static string ConvertFilterToString(FilterDefinition<T> filter) => filter
        .Render(BsonSerializer.SerializerRegistry.GetSerializer<T>(),
            BsonSerializer.SerializerRegistry
        ).AsBsonDocument.ToString();

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
        CacheTimestamp = Timestamp.UnixTime + seconds;
        return this;
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
    /// Used to combine multiple queries.  Slightly discouraged - preferred use is to modify an initial query instead.
    /// For example, if you need to look for a record with Foo.Bar = 5 or model.Foo = 10, use the FilterChain method
    /// ContainedIn(model => model.Foo, new [] { 5, 10 }) instead of two separate filters.  However, if the documents
    /// aren't at all related and you're looking for wildly different fields, the Or filter may use multiple indexes,
    /// so it can still be more performant in some situations - in such cases, use your judgment to decide whether or not
    /// it's more readable to just use a second MINQ chain entirely.
    /// </summary>
    /// <param name="builder">A filter method chain.</param>
    /// <returns>The RequestChain for method chaining.</returns>
    public RequestChain<T> Or(Action<FilterChain<T>> builder)
    {
        FilterChain<T> or = new FilterChain<T>();
        builder.Invoke(or);
        _filter = Builders<T>.Filter.Or(_filter, or.Filter);
        UpdateIndexWeights(or);
        return this;
    }
    
    /// <summary>
    /// Used to combine method chains for a request.  Moderately discouraged to use this, as you can accomplish more
    /// readable queries by just using the initial FilterChain instead.
    /// </summary>
    /// <param name="builder">A filter method chain.</param>
    /// <returns>The RequestChain for method chaining.</returns>
    public RequestChain<T> And(Action<FilterChain<T>> builder)
    {
        FilterChain<T> and = new FilterChain<T>();
        builder.Invoke(and);
        _filter = Builders<T>.Filter.And(_filter, and.Filter);
        UpdateIndexWeights(and);
        return this;
    }
    
    /// <summary>
    /// Used to create a negated filter chain.  It's strongly recommended to avoid using this unless necessary, as
    /// you can create easier-to-understand queries with negated equality operators with NotEqualTo.  This is combined with
    /// prior filter chains.
    /// </summary>
    /// <param name="builder">A filter method chain.</param>
    /// <returns>The RequestChain for method chaining.</returns>
    public RequestChain<T> Not(Action<FilterChain<T>> builder)
    {
        FilterChain<T> not = new FilterChain<T>();
        builder.Invoke(not);
        
        _filter = Builders<T>.Filter.And(_filter, Builders<T>.Filter.Not(not.Filter));
        UpdateIndexWeights(not);
        return this;
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
    /// Creates a filter to limit the data that is returned or affected in Mongo.
    /// </summary>
    /// <param name="query">A lambda expression that builds your filter chain.  The entire filter should be built
    /// with a method chain here.</param>
    /// <returns>A RequestChain object, which allows you to issue terminal commands such as Update() or ToList().</returns>
    public RequestChain<T> Where(Action<FilterChain<T>> query)
    {
        FilterChain<T> filter = new FilterChain<T>();
        query.Invoke(filter);
        
        if (_filter != null && _filter != Builders<T>.Filter.Empty)
            Log.Warn(Owner.Default, "Filter was not empty when Where() was called.  Where() overrides previous filters.  Is this intentional?");

        _filter = filter.Filter;
        UpdateIndexWeights(filter);
        return this;
    }

    /// <summary>
    /// Attempts to complete the RequestChain, disallowing its use again.  Once consumed, the filter is evaluated,
    /// which can result in the automatic creation of indexes if the query isn't covered.
    /// </summary>
    /// <exception cref="PlatformException">Thrown when the chain has already been consumed.</exception>
    private void Consume()
    {
        if (Consumed)
            throw new PlatformException("The RequestChain was previously consumed by another action.  This is not allowed to prevent accidental DB spam.");
        Consumed = true;
        EvaluateFilter();
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

        return _collection.CountDocuments(_filter);
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
        catch
        {
            Transaction?.TryAbort();
            throw;
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
        catch
        {
            Transaction?.TryAbort();
            throw;
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
        
        return FindWithLimitAndSort()
            .Project(Builders<T>.Projection.Expression(expression))
            .ToList()
            .ToArray();
    }
    
    /// <summary>
    /// Returns the results of your MINQ pipeline as a List.
    /// </summary>
    /// <returns>A list of matching models in the database.</returns>
    public List<T> ToList()
    {
        WarnOnUnusedEvents(nameof(ToList));
        Consume();

        if (!UseCache)
            return FindWithLimitAndSort().ToList();

        if (Parent.CheckCache(FilterAsString, out T[] data))
            return data.ToList();

        List<T> output = FindWithLimitAndSort().ToList();
        string f = ConvertFilterToString(_filter);
        Parent.Cache(_filter, output.ToArray(), CacheTimestamp, Transaction);
        return output;
    }

    /// <summary>
    /// Returns the results of your MINQ pipeline as an Array.  This requires an additional data transformation of
    /// List to Array.
    /// </summary>
    /// <returns>An array of matching models in the database.</returns>
    public T[] ToArray()
    {
        WarnOnUnusedEvents(nameof(ToArray));
        Consume();

        if (!UseCache)
            return FindWithLimitAndSort().ToList().ToArray();

        if (Parent.CheckCache(_filter, out T[] data))
            return data;
        
        T[] output = FindWithLimitAndSort().ToList().ToArray();
        Parent.Cache(_filter, output, CacheTimestamp, Transaction);
        return output;
    }
    
    /// <summary>
    /// Performs an update on all documents matching your specified filter.  Note that you can't set the same field twice
    /// with the same chain; for non-primitive types like arrays this will throw an exception.
    /// </summary>
    /// <param name="query">A lambda expression for an update chain builder.  Set all of your fields in one chain with it.</param>
    /// <returns>The number of records affected by the update.</returns>
    /// <exception cref="PlatformException">Thrown when there's a write conflict from updating a field more than once.</exception>
    /// <exception cref="MongoWriteException">Thrown when there's an unknown problem with the update.</exception>
    public long Update(Action<UpdateChain<T>> query)
    {
        WarnOnUnusedCache(nameof(Update));
        Consume();

        if (ShouldAbort(nameof(Update)))
            return 0;

        UpdateChain<T> updateChain = new UpdateChain<T>();
        query.Invoke(updateChain);
        _update = updateChain.Update;
        
        try
        {
            long output = (UsingTransaction
                ? _collection.UpdateMany(Transaction.Session, _filter, _update)
                : _collection.UpdateMany(_filter, _update)).ModifiedCount;
            
            FireAffectedEvent(output);
            
            return output;
        }
        catch (MongoWriteException)
        {
            Transaction?.TryAbort();
            throw;
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
    public T Upsert(Action<UpdateChain<T>> query = null)
    {
        WarnOnUnusedCache(nameof(Count));
        Consume();

        if (ShouldAbort(nameof(Upsert)))
            return default;

        UpdateChain<T> updateChain = new UpdateChain<T>();
        query?.Invoke(updateChain);
        _update = updateChain.Update;

        FindOneAndUpdateOptions<T> options = new FindOneAndUpdateOptions<T>
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
        catch
        {
            Transaction?.TryAbort();
            throw;
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

        ReplaceOptions options = new ReplaceOptions
        {
            IsUpsert = true
        };

        try
        {
            ReplaceOneResult result = UsingTransaction
                ? _collection.ReplaceOne(Transaction.Session, _filter, model, options)
                : _collection.ReplaceOne(_filter, model, options);

            if (result.ModifiedCount == 0)
                throw new PlatformException("MINQ Upsert(model) failed to replace the existing document.");
            
            FireAffectedEvent(1);

            return model;
        }
        catch
        {
            Transaction?.TryAbort();
            throw;
        }
    }
    #endregion Terminal Commands

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

    #region Index Management
    /// <summary>
    /// Runs automatic analysis on the filter the chain created.  If the filter is not covered by an index, this will
    /// try to create an appropriate one for it.
    /// </summary>
    private void EvaluateFilter()
    {
        MinqIndex[] existing = Parent.RefreshIndexes(out int next);
        MinqIndex suggested = new MinqIndex(_indexWeights);

        if (suggested.IsProbablyCoveredBy(existing))
            return;
        
        bool deliberateEmptyFilter = Minq<T>.Render(_filter) == Minq<T>.Render(Builders<T>.Filter.Empty);
        if (deliberateEmptyFilter)
            return;

        Explain(out MongoQueryStats stats);
        try
        {
            if (stats.IsFullyCovered)
                return;

            if (stats.IsPartiallyCovered)
                Log.Info(Owner.Will, "A MINQ query was partially covered by existing indexes; a new index will be added");
            else if (stats.IsNotCovered)
                Log.Error(Owner.Will, "A MINQ query is not covered by any index; a new index will be added");
            
            suggested.Name = $"{MinqIndex.INDEX_PREFIX}{next}";
            CreateIndexModel<BsonDocument> model = suggested.GenerateIndexModel();
            Parent.GenericCollection.Indexes.CreateOne(model);
            Log.Info(Owner.Will, "MINQ automatically created an index", data: new
            {
                Collection = _collection.CollectionNamespace.CollectionName,
                IndexName = suggested.Name,
                Filter = (RumbleJson)RenderedFilter
            });
        }
        catch (Exception e)
        {
            Log.Error(Owner.Will, stats.DocumentsReturned > 1_000 
                ? "Unable to create automatic MINQ index, and the query scanned many documents; investigation required" 
                : "Unable to create automatic MINQ index; investigation possibly needed",
                data: new
                {
                    Filter = (RumbleJson)RenderedFilter,
                    Stats = stats
                }, exception: e
            );
        }
    }


    public RequestChain<T> Sort(Action<SortChain<T>> sort)
    {
        if (_sort != null)
            Log.Warn(Owner.Default, $"Only one call to MINQ {nameof(Sort)} can be honored per request.  Combine them into one call.");
        SortChain<T> chain = new SortChain<T>();
        sort.Invoke(chain);

        _sort = chain.Sort;
        return this;
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
            BsonDocument command = new BsonDocument
            {
                { "explain", new BsonDocument { { "find", _collection.CollectionNamespace.CollectionName }, { "filter", RenderedFilter } } }
            };

            stats = new MongoQueryStats(_collection.Database.RunCommand<BsonDocument>(command));

        }
        catch (Exception e)
        {
            Log.Error(Owner.Will, "Unable to parse Mongo explanation", exception: e);
        }
    }
    #endregion Index Management
}






















