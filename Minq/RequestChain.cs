using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Linq.Expressions;
using System.Transactions;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Core.Operations;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;
using ReturnDocument = MongoDB.Driver.Core.Operations.ReturnDocument;

namespace Rumble.Platform.Common.Minq;

public class RequestChain<T> where T : PlatformCollectionDocument
{
    private IMongoCollection<T> _collection => Parent.Collection;
    internal FilterDefinition<T> _filter { get; set; }
    internal UpdateDefinition<T> _update { get; set; }
    private Minq<T> Parent { get; set; }
    private int _limit { get; set; }
    private bool Consumed { get; set; }
    
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
    
    public RequestChain(Minq<T> parent, FilterDefinition<T> filter = null)
    {
        _filter = filter ?? Builders<T>.Filter.Empty;
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
        return this;
    }
    
    public RequestChain<T> And(Action<FilterChain<T>> builder)
    {
        FilterChain<T> and = new FilterChain<T>();
        builder.Invoke(and);
        _filter = Builders<T>.Filter.And(_filter, and.Filter);
        return this;
    }
    
    public RequestChain<T> Not(Action<FilterChain<T>> builder)
    {
        FilterChain<T> not = new FilterChain<T>();
        builder.Invoke(not);
        
        _filter = Builders<T>.Filter.And(_filter, Builders<T>.Filter.Not(not.Filter));
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

        return this;
    }

    private void EnforceNotConsumed()
    {
        if (Consumed)
            throw new PlatformException("The RequestChain was previously consumed by another action.  This is not allowed to prevent accidental DB spam.");
        Consumed = true;
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
        EnforceNotConsumed();
        
        return _collection.CountDocuments(_filter);
    }
    
    public long Delete()
    {
        EnforceNotConsumed();
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
        EnforceNotConsumed();

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
        EnforceNotConsumed();
        
        return FindWithLimit()
            .Project(Builders<T>.Projection.Expression(expression))
            .ToList()
            .ToArray();
    }
    
    public List<T> ToList()
    {
        WarnOnUnusedEvents(nameof(ToList));
        EnforceNotConsumed();
        
        return FindWithLimit().ToList();
    }
    
    public long Update(Action<UpdateChain<T>> query)
    {
        EnforceNotConsumed();

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
        EnforceNotConsumed();

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
}
