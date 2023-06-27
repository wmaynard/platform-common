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
    private Minq<T> Parent { get; set; }
    private int _limit { get; set; }
    private bool Consumed { get; set; }

    private BsonDocument RenderedFilter => _filter.Render(BsonSerializer.SerializerRegistry.GetSerializer<T>(), BsonSerializer.SerializerRegistry);
    
    internal Transaction Transaction { get; set; }
    internal bool UsingTransaction => Transaction != null;
    
    private EventHandler<RecordsAffectedArgs> _onRecordsAffected;
    private EventHandler<RecordsAffectedArgs> _onNoneAffected;
    private EventHandler<RecordsAffectedArgs> _onTransactionAborted;
    
    
    private string FilterAsString => _filter
        .Render(BsonSerializer.SerializerRegistry.GetSerializer<T>(),
        BsonSerializer.SerializerRegistry
    ).AsBsonDocument.ToString();
    
    private string UpdateAsString => _update.Render(
        BsonSerializer.SerializerRegistry.GetSerializer<T>(),
        BsonSerializer.SerializerRegistry
    ).AsBsonDocument.ToString();

    private void UpdateIndexWeights(FilterChain<T> filter)
    {
        if (filter == null)
            return;
        _indexWeights ??= new Dictionary<string, int>();
        foreach (KeyValuePair<string, int> pair in filter.IndexWeights.Where(pair => !_indexWeights.TryAdd(pair.Key, pair.Value)))
            _indexWeights[pair.Key] += pair.Value;
    }
    
    public RequestChain(Minq<T> parent, FilterChain<T> filterChain = null)
    {
        UpdateIndexWeights(filterChain);
        _filter = filterChain?.Filter ?? Builders<T>.Filter.Empty;
        Parent = parent;
    }
    
    public RequestChain<T> OnTransactionEnded(Action action)
    {
        if (action != null)
            _onTransactionAborted += (sender, args) => action.Invoke();
        return this;
    }

    public RequestChain<T> OnRecordsAffected(Action<RecordsAffectedArgs> result)
    {
        if (result != null)
            _onRecordsAffected += (sender, args) =>
            {
                result.Invoke(args);
            };
        return this;
    }

    public RequestChain<T> OnNoneAffected(Action<RecordsAffectedArgs> result)
    {
        if (result != null)
            _onNoneAffected += (sender, args) =>
            {
                result.Invoke(args);
            };
        return this;
    }

    public RequestChain<T> Limit([Range(0, int.MaxValue)] int limit)
    {
        _limit = limit;
        return this;
    }

    public RequestChain<T> Or(Action<FilterChain<T>> builder)
    {
        FilterChain<T> or = new FilterChain<T>();
        builder.Invoke(or);
        _filter = Builders<T>.Filter.Or(_filter, or.Filter);
        UpdateIndexWeights(or);
        return this;
    }
    
    public RequestChain<T> And(Action<FilterChain<T>> builder)
    {
        FilterChain<T> and = new FilterChain<T>();
        builder.Invoke(and);
        _filter = Builders<T>.Filter.And(_filter, and.Filter);
        UpdateIndexWeights(and);
        return this;
    }
    
    public RequestChain<T> Not(Action<FilterChain<T>> builder)
    {
        FilterChain<T> not = new FilterChain<T>();
        builder.Invoke(not);
        
        _filter = Builders<T>.Filter.And(_filter, Builders<T>.Filter.Not(not.Filter));
        UpdateIndexWeights(not);
        return this;
    }

    private IFindFluent<T, T> FindWithLimit() => _limit switch
    {
        <= 0 when UsingTransaction => _collection.Find(Transaction.Session, _filter),
        <= 0 => _collection.Find(_filter),
        _ when UsingTransaction => _collection.Find(Transaction.Session, _filter).Limit(_limit),
        _ => _collection.Find(_filter).Limit(_limit)
    };

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

    private void Consume()
    {
        if (Consumed)
            throw new PlatformException("The RequestChain was previously consumed by another action.  This is not allowed to prevent accidental DB spam.");
        Consumed = true;
        EvaluateFilter();
    }
    
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

    private void WarnOnUnusedEvents(string methodName)
    {
        if (_onNoneAffected != null)
            Log.Warn(Owner.Default, $"{nameof(OnNoneAffected)} will not have any effect on {methodName}.  Remove the link in the method chain.");
        if (_onRecordsAffected != null)
            Log.Warn(Owner.Default, $"{nameof(OnRecordsAffected)} will not have any effect on {methodName}.  Remove the link in the method chain.");
        if (_onTransactionAborted != null)
            Log.Warn(Owner.Default, $"{nameof(OnTransactionEnded)} will not have any effect on {methodName}.  Remove the link in the method chain.");
    }
    
    #region TerminalCommands
    /// <summary>
    /// Returns the number of documents using the filter built from method-chaining.
    /// </summary>
    /// <returns>The number of documents matching the filter.</returns>
    public long Count()
    {
        WarnOnUnusedEvents(nameof(Count));
        Consume();
        
        return _collection.CountDocuments(_filter);
    }
    
    public long Delete()
    {
        Consume();
        if (ShouldAbort(nameof(Delete)))
            return 0;
        
        Consumed = true;
        long output = (UsingTransaction
            ? _collection.DeleteMany(Transaction.Session, _filter)
            : _collection.DeleteMany(_filter)).DeletedCount;
        
        FireAffectedEvent(output);

        return output;
    }
    
    public void Insert(params T[] models)
    {
        Consume();

        if (ShouldAbort(nameof(Insert)))
            return;

        T[] toInsert = models.Where(model => model != null).ToArray();
        if (!toInsert.Any())
            throw new PlatformException("You must provide at least one model to insert.  Null objects are ignored.");
        
        if (UsingTransaction)
            _collection.InsertMany(Transaction.Session, toInsert);
        else
            _collection.InsertMany(toInsert);
        
        FireAffectedEvent(models.Length);
    }
    
    public U[] Project<U>(Expression<Func<T, U>> expression)
    {
        WarnOnUnusedEvents(nameof(Project));
        Consume();
        
        return FindWithLimit()
            .Project(Builders<T>.Projection.Expression(expression))
            .ToList()
            .ToArray();
    }
    
    public List<T> ToList()
    {
        WarnOnUnusedEvents(nameof(ToList));
        Consume();
        
        return FindWithLimit().ToList();
    }
    
    public long Update(Action<UpdateChain<T>> query)
    {
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
        catch (MongoWriteException e)
        {
            if (e.WriteError.Code == 40 || e.Message.Contains("conflict"))
                throw new PlatformException("Write conflict encountered.  Check that you aren't updating the same field multiple times in one query.");
            throw;
        }
    }

    public T Upsert(Action<UpdateChain<T>> query)
    {
        Consume();

        if (ShouldAbort(nameof(Upsert)))
            return default;

        UpdateChain<T> updateChain = new UpdateChain<T>();
        query.Invoke(updateChain);
        _update = updateChain.Update;

        FindOneAndUpdateOptions<T> options = new FindOneAndUpdateOptions<T>
        {
            IsUpsert = true,
            ReturnDocument = MongoDB.Driver.ReturnDocument.After
        };

        try
        {
            T output = (UsingTransaction)
                ? _collection.FindOneAndUpdate(Transaction.Session, _filter, _update, options)
                : _collection.FindOneAndUpdate(_filter, _update, options);
            
            FireAffectedEvent(1);

            return output;
        }
        catch (MongoWriteException e)
        {
            if (e.WriteError.Code == 40 || e.Message.Contains("conflict"))
                throw new PlatformException("Write conflict encountered.  Check that you aren't updating the same field multiple times in one query.");
            throw;
        }
    }
    #endregion
    
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

        Explain(out MongoQueryStats stats);
        try
        {
            if (stats.IsFullyCovered)
                return;
            
            if (stats.IsPartiallyCovered)
                Log.Info(Owner.Will, "A MINQ query was partially covered by existing indexes; a new index will be added");
            else if (stats.IsNotCovered)
                Log.Error(Owner.Will, "A MINQ query is not covered by any index; a new index will be added");
            
            // Look for indexes with a name of minq_X, then use X + 1 as the next index name.
            // int next = existing
            //     .Select(index => index.Name)
            //     .Where(name => !string.IsNullOrWhiteSpace(name) && name.StartsWith(MinqIndex.INDEX_PREFIX))
            //     .Select(name => int.TryParse(name[MinqIndex.INDEX_PREFIX.Length..], out int output)
            //         ? output
            //         : 0
            //     )
            //     .Max();
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
    
    // /// <summary>
    // /// Loads all of the currently-existing indexes on the collection.
    // /// </summary>
    // /// <returns>An array of indexes that exist on the collection.</returns>
    // private MinqIndex[] RefreshIndexes()
    // {
    //     List<MinqIndex> output = new List<MinqIndex>();
    //     
    //     using (IAsyncCursor<BsonDocument> cursor = _collection.Indexes.List())
    //         while (cursor.MoveNext())
    //             output.AddRange(cursor.Current.Select(doc => new MinqIndex(doc)));
    //
    //     return output
    //         .Where(index => index != null)
    //         .ToArray();
    // }
    #endregion Index Management
}






















