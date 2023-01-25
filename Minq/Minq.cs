using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Minq;

/* Welcome to MINQ - The Mongo Integrated Query!
                                _,-/"---,
         ;"""""""""";         _/;; ""  <@`---v
       ; :::::  ::  "\      _/ ;;  "    _.../
      ;"     ;;  ;;;  \___/::    ;;,'""""
     ;"          ;;;;.  ;;  ;;;  ::/
    ,/ / ;;  ;;;______;;;  ;;; ::,/
    /;;V_;;   ;;;       \       /
    | :/ / ,/            \_ "")/
    | | / /"""=            \;;\""=
    ; ;{::""""""=            \"""=
 ;"""";
 \/"""
 
 Source: https://ascii.co.uk/art/weasel (Ermine)
 */

public class Minq<T> where T : PlatformCollectionDocument
{
    internal static MongoClient Client { get; set; }
    internal readonly IMongoCollection<T> Collection;
    // internal Transaction Transaction { get; set; }
    // internal bool UsingTransaction => Transaction != null;
    // internal EventHandler<RecordsAffectedArgs> _onRecordsAffected;
    // internal EventHandler<RecordsAffectedArgs> _onTransactionAborted;

    public Minq(IMongoCollection<T> collection)
    {
        Collection = collection;
    }

    /// <summary>
    /// Unlike other methods, this allows you to build a LINQ query to search the database.
    /// While this can be an easy way to search for documents, keep in mind that being a black box, the performance
    /// of such a query is somewhat of a mystery.  Furthermore, using this does impact planned features for Minq, such as
    /// automatically building indexes based on usage with reflection.
    /// </summary>
    /// <returns>A Mongo type that can be used as if it was a LINQ query; can only be used for reading data, not updating.</returns>
    public IMongoQueryable<T> AsLinq() => Collection.AsQueryable();



    public RequestChain<T> WithTransaction(Transaction transaction)
    {
        return new RequestChain<T>(this)
        {
            Transaction = transaction
        };
    }

    public RequestChain<T> WithTransaction(out Transaction transaction)
    {
        transaction = new Transaction(Client.StartSession());
        return new RequestChain<T>(this)
        {
            Transaction = transaction
        };
    }
    
    public RequestChain<T> OnTransactionAborted(Action action) => new RequestChain<T>(this).OnTransactionEnded(action);

    public RequestChain<T> OnRecordsAffected(Action<RecordsAffectedArgs> result) => new RequestChain<T>(this).OnRecordsAffected(result);

    // public RequestChain<T> Filter(params FilterDefinition<T>[] filters)
    // {
    //     if (!filters.Any())
    //         return new RequestChain<T>(this);
    //     return new RequestChain<T>(this)
    //     {
    //         _filter = filters.Any()
    //             ? Builders<T>.Filter.Empty
    //             : Builders<T>.Filter.And(filters)
    //     };
    // }

    // public RequestChain<T> Filter<TField>(FieldDefinition<T, TField> field, TField value)
    // {
    //     return new RequestChain<T>(this)
    //     {
    //         _filter = Builders<T>.Filter.Gt(field, value)
    //     };
    // }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="query"></param>
    /// <returns></returns>
    public RequestChain<T> Where(Action<FilterChain<T>> query)
    {
        FilterChain<T> filter = new FilterChain<T>();
        query.Invoke(filter);
        
        return new RequestChain<T>(this, filter.Filter);
    }
    
    public void Insert(params T[] models)
    {
        if (!models.Any())
            throw new Exception();
        
        Collection.InsertMany(models);
    }
    
    public static Minq<T> Connect(string collectionName)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new PlatformException("Collection name cannot be a null or empty string.");
        if (string.IsNullOrWhiteSpace(PlatformEnvironment.MongoConnectionString))
            throw new PlatformException("Mongo connection string cannot be a null or empty string.");
        if (string.IsNullOrWhiteSpace(PlatformEnvironment.MongoDatabaseName))
            throw new PlatformException("Mongo connection string must include a database name.");
        
        Client ??= new MongoClient(PlatformEnvironment.MongoConnectionString);
        
        return new Minq<T>(Client
            .GetDatabase(PlatformEnvironment.MongoDatabaseName)
            .GetCollection<T>(collectionName)
        );
    }

    public long UpdateAll(Action<UpdateChain<T>> action)
    {
        RequestChain<T> req = new RequestChain<T>(this);
        
        return req.Update(action);
    }

    public bool Update(T document, bool insertIfNotFound = true) => Collection.ReplaceOne(
        filter: $"{{_id:ObjectId('{document.Id}')}}", 
        replacement: document, options: new ReplaceOptions()
        {
            IsUpsert = insertIfNotFound
        }
    ).ModifiedCount > 0;

    public static string Render(Expression<Func<T, object>> field) => new ExpressionFieldDefinition<T>(field)
        .Render(
            documentSerializer: BsonSerializer.SerializerRegistry.GetSerializer<T>(),
            serializerRegistry: BsonSerializer.SerializerRegistry
        ).FieldName;

    
    
#if DEBUG
    public long DeleteAll() => new RequestChain<T>(this).Delete();
    public List<T> ListAll() => new RequestChain<T>(this).ToList();

    public U[] Project<U>(Expression<Func<T, U>> expression) => new RequestChain<T>(this).Project(expression);
#endif
}
