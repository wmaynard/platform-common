using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using RCL.Data;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
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
    public class CachedResult : PlatformCollectionDocument
    {
        internal const string DB_KEY_MINQ_CACHE_FILTER = "query";
        internal const string DB_KEY_MINQ_CACHE_DATA = "data";
        internal const string DB_KEY_MINQ_CACHE_EXPIRATION = "exp";
            
        [BsonElement(DB_KEY_MINQ_CACHE_FILTER)]
        internal string FilterAsString { get; set; }
        [BsonElement(DB_KEY_MINQ_CACHE_DATA)]
        internal T[] Results { get; set; }
        [BsonElement(DB_KEY_MINQ_CACHE_EXPIRATION)]
        internal long Expiration { get; set; }
    }
    internal static MongoClient Client { get; set; }
    internal readonly IMongoCollection<T> Collection;
    internal readonly IMongoCollection<CachedResult> CachedQueries;
    private bool CacheIndexesExist { get; set; }
    public string CollectionName { get; init; }

    private IMongoCollection<BsonDocument> _collectionGeneric;

    internal IMongoCollection<BsonDocument> GenericCollection => _collectionGeneric ??= Collection.Database.GetCollection<BsonDocument>(Collection.CollectionNamespace.CollectionName);

    public Minq(IMongoCollection<T> collection, IMongoCollection<CachedResult> cache)
    {
        Collection = collection;
        CollectionName = collection.CollectionNamespace.CollectionName;
        CachedQueries = cache;
    }

    internal bool CheckCache(FilterDefinition<T> filter, out T[] cachedData)
    {
        cachedData = Array.Empty<T>();

        CachedResult result = CachedQueries
            .Find(
                Builders<CachedResult>.Filter.And(
                    Builders<CachedResult>.Filter.Eq(cache => cache.FilterAsString, Render(filter)),
                    Builders<CachedResult>.Filter.Gte(cache => cache.Expiration, Timestamp.Now)
                )
            )
            .ToList()
            .FirstOrDefault();

        cachedData = result?.Results;
        bool output = result != null;
        
        // Delete all the cache entries over one day old.
        if (output)
            CachedQueries.DeleteMany(Builders<CachedResult>.Filter.Lt(cache => cache.Expiration, Timestamp.Now - 60 * 60 * 24));

        return result != null;
    }

    /// <summary>
    /// This is brittle and ugly and I hate it, but unfortunately it's a challenge to get Mongo to work well with C# typing
    /// here.  This indexed class comes from an intermediary library (platform-common) and uses generic types, and getting
    /// Mongo to figure out the serialization for it has proven to be a very expensive use of time.  Consequently the approach
    /// is to manually craft the index as a BsonDocument, then check existing indexes to see if a matching one exists,
    /// and finally create the index.... using a strongly typed CreateIndexModel that for some reason doesn't fall over
    /// when everything else does.... but if we use the BsonDocument we get an Obsolete warning.  Isn't Mongo fun?
    /// </summary>
    private void TryCreateCacheIndexIfNecessary()
    {
        if (CacheIndexesExist)
            return;
        try
        {
            BsonDocument[] existing = CachedQueries.Indexes.List().ToList().ToArray();
            BsonDocument desired = new BsonDocument
            {
                { "key", new BsonDocument
                {
                    { CachedResult.DB_KEY_MINQ_CACHE_FILTER, 1 },
                    { CachedResult.DB_KEY_MINQ_CACHE_EXPIRATION, -1 }
                }},
                {
                    "options", new BsonDocument
                    {
                        { "background", true }
                    }
                }
            };

            if (!existing
                    .Select(db => db.TryGetValue("key", out BsonValue output) ? output : null)
                    .Where(value => value != null)
                    .Any(value => value == desired.GetValue("key")))
            {
                CachedQueries.Indexes.CreateOne(new CreateIndexModel<CachedResult>(
                    keys: Builders<CachedResult>.IndexKeys.Combine(
                        Builders<CachedResult>.IndexKeys.Ascending(cache => cache.FilterAsString),
                        Builders<CachedResult>.IndexKeys.Descending(cache => cache.Expiration)
                    ),
                    options: new CreateIndexOptions
                    {
                        Background = true
                    }
                ));
            }
            CacheIndexesExist = true;
        }
        catch (Exception e)
        {
            Log.Warn(Owner.Will, "Unable to create index for MINQ cache.", data: new
            {
                Cache = CachedQueries.CollectionNamespace.CollectionName
            }, exception: e);
        }
    }

    internal void Cache(FilterDefinition<T> filter, T[] results, long expiration, Transaction transaction = null)
    {
        TryCreateCacheIndexIfNecessary();
        
        FindOneAndUpdateOptions<CachedResult> options = new FindOneAndUpdateOptions<CachedResult>
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After
        };
        
        if (transaction?.Session != null)
            CachedQueries
                .FindOneAndUpdate(
                    session: transaction.Session,
                    filter: Builders<CachedResult>.Filter.Eq(cache => cache.FilterAsString, Render(filter)),
                    update: Builders<CachedResult>.Update
                        .Set(cache => cache.Results, results)
                        .Set(cache => cache.Expiration, expiration),
                    options
                );
        else
            CachedQueries
                .FindOneAndUpdate(
                    filter: Builders<CachedResult>.Filter.Eq(cache => cache.FilterAsString, Render(filter)),
                    update: Builders<CachedResult>.Update
                        .Set(cache => cache.Results, results)
                        .Set(cache => cache.Expiration, expiration),
                    options
                );
    }

    /// <summary>
    /// Unlike other methods, this allows you to build a LINQ query to search the database.
    /// While this can be an easy way to search for documents, keep in mind that being a black box, the performance
    /// of such a query is somewhat of a mystery.  Furthermore, using this does impact planned features for Minq, such as
    /// automatically building indexes based on usage with reflection.
    /// </summary>
    /// <returns>A Mongo type that can be used as if it was a LINQ query; can only be used for reading data, not updating.</returns>
    public IMongoQueryable<T> AsLinq() => Collection.AsQueryable();
    
    /// <summary>
    /// Uses an existing Transaction to use for Mongo queries.  Transactions are generally not supported on localhost; use a
    /// deployed environment connection string to test them.  If you need a new Transaction, use the other overload with
    /// an out parameter.
    /// </summary>
    /// <param name="transaction">The MINQ Transaction to use.</param>
    /// <param name="abortOnFailure">If true and MINQ encounters an exception, the transaction will be aborted.</param>
    /// <returns>A new RequestChain for method chaining.</returns>
    public RequestChain<T> WithTransaction(Transaction transaction, bool abortOnFailure = true) => new RequestChain<T>(this)
    {
        Transaction = transaction,
        AbortTransactionOnFailure = abortOnFailure
    };

    /// <summary>
    /// Creates a new Transaction to use for Mongo queries.  Transactions are generally not supported on localhost; use a
    /// deployed environment connection string to test them.
    /// </summary>
    /// <param name="transaction">The MINQ Transaction to use in future queries.</param>
    /// <param name="abortOnFailure">If true and MINQ encounters an exception, the transaction will be aborted.</param>
    /// <returns>A new RequestChain for method chaining.</returns>
    public RequestChain<T> WithTransaction(out Transaction transaction, bool abortOnFailure = true)
    {
        transaction = !PlatformEnvironment.MongoConnectionString.Contains("localhost")
            ? new Transaction(Client.StartSession())
            : null;

        return new RequestChain<T>(this)
        {
            Transaction = transaction,
            AbortTransactionOnFailure = abortOnFailure
        };
    }
    
    public RequestChain<T> OnTransactionAborted(Action action) => new RequestChain<T>(this).OnTransactionAborted(action);

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
        
        return new RequestChain<T>(this, filter);
    }

    /// <summary>
    /// Creates a RequestChain for use in building a request in branching code paths.  Frequent use is discouraged.
    /// This functionality may be removed at a later date.
    /// </summary>
    /// <returns></returns>
    public RequestChain<T> CreateRequestChain() => new RequestChain<T>(this);

    public T FirstOrDefault(Action<FilterChain<T>> query) => Where(query).FirstOrDefault();

    public RequestChain<T> ExactId(string id) => Where(query => query.EqualTo(doc => doc.Id, id));

    /// <summary>
    /// Returns a request that will affect all records on the database.  You can override this with further queries,
    /// but if you're going to do that, there's no point to this call.
    /// </summary>
    /// <returns>The RequestChain for method chaining.</returns>
    public RequestChain<T> All() => new RequestChain<T>(this, new FilterChain<T>().All());

    public long Count(Action<FilterChain<T>> query)
    {
        FilterChain<T> filter = new FilterChain<T>();
        query.Invoke(filter);

        return Collection.CountDocuments(filter.Filter);
    }
    
    public void Insert(params T[] models)
    {
        T[] toInsert = models.Where(model => model != null).ToArray();
        if (!toInsert.Any())
            throw new PlatformException("You must provide at least one model to insert.  Null objects are ignored.");
        
        Collection.InsertMany(toInsert);
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
        // Adds a global BsonIgnoreExtraElements
        ConventionRegistry.Register("IgnoreExtraElements", new ConventionPack
        {
            new IgnoreExtraElementsConvention(true)
        }, filter: t => true);

        IMongoDatabase db = Client.GetDatabase(PlatformEnvironment.MongoDatabaseName);

        return new Minq<T>(db.GetCollection<T>(collectionName), db.GetCollection<CachedResult>($"{collectionName}_cache"));
    }

    public long UpdateAll(Action<UpdateChain<T>> action)
    {
        RequestChain<T> req = new RequestChain<T>(this);
        
        return req.Update(action);
    }

    public bool Update(T document, bool insertIfNotFound = true) => Collection.ReplaceOne(
        filter: $"{{_id:ObjectId('{document.Id}')}}", 
        replacement: document, options: new ReplaceOptions
        {
            IsUpsert = insertIfNotFound
        }
    ).ModifiedCount > 0;



    private MinqIndex[] PredefinedIndexes { get; set; }

    public void DefineIndex(Action<IndexChain<T>> builder) => DefineIndexes(builder);
    public void DefineIndexes(params Action<IndexChain<T>>[] builders)
    {
        MinqIndex[] existing = RefreshIndexes(out int next);
        
        foreach (Action<IndexChain<T>> builder in builders)
        {
            IndexChain<T> chain = new IndexChain<T>();
            builder.Invoke(chain);

            MinqIndex index = chain.Build();
            if (!index.IsProbablyCoveredBy(existing))
                TryCreateIndex(index);
            else if (index.HasConflict(existing, out string name))
            {
                GenericCollection.Indexes.DropOne(name);
                index.Name ??= $"{MinqIndex.INDEX_PREFIX}{next++}";
                TryCreateIndex(index);
            }
        }
    }

    internal void CreateIndex(MinqIndex index)
    {
        Log.Local(Owner.Will, "Creating an index", emphasis: Log.LogType.ERROR);
        CreateIndexModel<BsonDocument> model = index.GenerateIndexModel();
        GenericCollection.Indexes.CreateOne(model);
    }

    internal void TryCreateIndex(MinqIndex index)
    {
        try
        {
            CreateIndex(index);
        }
        catch (Exception e)
        {
            Log.Error(Owner.Default, "Unable to create mongo index", data: new
            {
                MinqIndex = index
            }, exception: e);
        }
    }
    
    /// <summary>
    /// Loads all of the currently-existing indexes on the collection.
    /// </summary>
    /// <returns>An array of indexes that exist on the collection.</returns>
    internal MinqIndex[] RefreshIndexes(out int nextIndex)
    {
        nextIndex = 0;
        List<MinqIndex> output = new List<MinqIndex>();
        
        using (IAsyncCursor<BsonDocument> cursor = Collection.Indexes.List())
            while (cursor.MoveNext())
                output.AddRange(cursor.Current.Select(doc => new MinqIndex(doc)));

        if (!output.Any())
            return Array.Empty<MinqIndex>();

        try
        {
            nextIndex = output
                .Select(index => index.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name) && name.StartsWith(MinqIndex.INDEX_PREFIX))
                .Select(name => int.TryParse(name[MinqIndex.INDEX_PREFIX.Length..], out int number)
                    ? number
                    : 0
                )
                .Max() + 1;
        }
        catch (InvalidOperationException) { } // if we hit this, no index has a format of minq_#
        
        return output
            .Where(index => index != null)
            .ToArray();
    }
    
    public static string Render(Expression<Func<T, object>> field) => new ExpressionFieldDefinition<T>(field)
        .Render(
            documentSerializer: BsonSerializer.SerializerRegistry.GetSerializer<T>(),
            serializerRegistry: BsonSerializer.SerializerRegistry
        ).FieldName;
    internal static string Render<U>(Expression<Func<T, U>> field) => new ExpressionFieldDefinition<T>(field)
        .Render(
            documentSerializer: BsonSerializer.SerializerRegistry.GetSerializer<T>(),
            serializerRegistry: BsonSerializer.SerializerRegistry
        ).FieldName;
    private static string Render(Expression<Func<CachedResult, object>> field) => new ExpressionFieldDefinition<CachedResult>(field)
        .Render(
            documentSerializer: BsonSerializer.SerializerRegistry.GetSerializer<CachedResult>(),
            serializerRegistry: BsonSerializer.SerializerRegistry
        ).FieldName;

    internal static string Render(FilterDefinition<T> filter) => filter.Render(
        documentSerializer: BsonSerializer.SerializerRegistry.GetSerializer<T>(),
        serializerRegistry: BsonSerializer.SerializerRegistry
    ).ToString();

    internal static bool TryRender(FilterDefinition<T> filter, out RumbleJson asJson, out string asString)
    {
        asJson = null;
        asString = null;
        
        try
        {
            asString = Render(filter);
            asJson = asString;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Serializes an UpdateDefinition as a flat string.  This can be useful for debugging purposes but serves no other important
    /// function at the time of this writing.
    /// </summary>
    /// <param name="update">The UpdateDefinition to serialize.</param>
    /// <returns>A string representation of the UpdateDefinition.</returns>
    internal static string Render(UpdateDefinition<T> update) => update.Render(
        documentSerializer: BsonSerializer.SerializerRegistry.GetSerializer<T>(),
        serializerRegistry: BsonSerializer.SerializerRegistry
    ).ToString();
    
#if DEBUG
    public long DeleteAll() => new RequestChain<T>(this).Delete();
    public List<T> ListAll() => new RequestChain<T>(this).ToList();

    public U[] Project<U>(Expression<Func<T, U>> expression) => new RequestChain<T>(this).Project(expression);
#endif

    /// <summary>
    /// Removes elements from a collection using a filter chain for an embedded object.  Note that this affects all
    /// documents in the database.
    /// </summary>
    /// <param name="model">An expression that selects the collection object to query.</param>
    /// <param name="query">The query to build the filter to remove items.</param>
    /// <typeparam name="U"></typeparam>
    /// <returns>The count of objects removed from all documents.</returns>
    /// TODO: Add this to RequestChain to enable Transactions
    public long RemoveElements<U>(Expression<Func<T, IEnumerable<U>>> model, Action<FilterChain<U>> query)
    {
        FilterChain<U> filter = new FilterChain<U>();
        query.Invoke(filter);
        
        return Collection
            .UpdateMany(
                filter: Builders<T>.Filter.ElemMatch(model, filter.Filter),
                update: Builders<T>.Update.PullFilter(model, filter.Filter)
            ).ModifiedCount;
    }

    /// <summary>
    /// Replaces the document in the database.  Note that this does not insert a document if one does not exist. 
    /// </summary>
    /// <param name="document"></param>
    /// TODO: Add to RequestChain to enable transactions.
    public void Replace(T document)
    {
        Collection.ReplaceOne(Builders<T>.Filter.Eq(dbDocument => dbDocument.Id, document.Id), document, new ReplaceOptions
        {
            IsUpsert = false
        });
    }

    // public long RemoveElements<U>(Expression<Func<T, IEnumerable<U>>> query, FilterDefinition<U> filter) => Collection
    //     .UpdateMany(
    //         filter: Builders<T>.Filter.ElemMatch(query, filter),
    //         update: Builders<T>.Update.PullFilter(query, filter)
    //     ).ModifiedCount;

    /// <summary>
    /// Searches all text fields of your model.  Caution: this will create an index on all text fields, which can be
    /// extremely expensive.  This is a WIP feature; if you need performance, consider writing your own at this time.
    /// A hard limit of 1000 records is returned currently.  Since the records are not evaluated for relevance before that,
    /// if your search turns up with more than 1000 results, you may not get the one you're looking for.
    /// </summary>
    /// <param name="term">The term to search for.</param>
    /// <returns>An array of models matching your search.</returns>
    public T[] Search(string term)
    {
        MemberInfoAccess[] infos = GetStringAccessors(typeof(T));
        Expression<Func<T, object>>[] info = infos
            .Select(ci =>
            {
                try
                {
                    UnaryExpression body = Expression.Convert(ci.Accessor, typeof(object));
                    ParameterExpression param = Expression.Parameter(typeof(T), typeof(T).Name);
                    Expression<Func<T, object>> output = Expression.Lambda<Func<T, object>>(body, param);
                    

                    // TODO: By default, the Mongo driver can't handle this.  There's an apparent bug in the following file:
                    // MongoDB.Driver.Linq.Linq3Implementation.Misc.SymbolTable.
                    // In TryGetSymbol(), the if condition is:
                    //     if (s.Parameter == parameter).
                    // However, this object equality means that only expressions created from Builders<T> work; if you
                    // create them separately, as we've done here, you'll get an exception.  It's possible to fix this by
                    // changing the conditional to:
                    //     if (s.Parameter.Type == parameter.Type)
                    // Unfortunately, this likely requires us to maintain our own fork.  It's unknown currently if there's
                    // a way to get Builders<T> to work with us or otherwise let us use our own reflected expressions.
                    // Render(output);  // this is just a test to see if Mongo can validate our expression.
                    
                    return output;
                }
                catch (Exception e)
                {
                    Log.Warn(Owner.Will, "Unable to create MINQ accessor expression", exception: e);
                    return null;
                }
            })
            .ToArray();

        RequestChain<T> request = new RequestChain<T>(this);

        foreach (Expression<Func<T, object>> expression in info)
            request.Or(builder => builder.ContainsSubstring(expression, term));

        request.Limit(1_000);

        List<T> output = request.ToList();
        // TODO: Weigh and score results, return in descending order.
        // TODO: Create Search() overload with specific fields

        return output.ToArray();
    }

    /// <summary>
    /// Returns an array of objects representing properties and fields of a given type, along with the expression accessor
    /// required for Mongo.
    /// </summary>
    /// <param name="type"></param>
    /// <param name="ex"></param>
    /// <returns></returns>
    private MemberInfoAccess[] GetStringAccessors(IReflect type, Expression ex = null)
    {
        List<MemberInfo> members = new List<MemberInfo>();
        List<MemberInfoAccess> cInfo = new List<MemberInfoAccess>();
        try
        {
            PropertyInfo[] props = type
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Union(type
                    .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(prop => prop.GetCustomAttribute<BsonElementAttribute>() != null)
                )
                .Where(prop => prop.GetCustomAttribute<BsonIgnoreAttribute>() == null)
                .ToArray();

            FieldInfo[] fields = type
                .GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Union(type
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(field => field.GetCustomAttribute<BsonElementAttribute>() != null)
                )
                .Where(prop => prop.GetCustomAttribute<BsonIgnoreAttribute>() == null)
                .ToArray();

            PropertyInfo[] stringProps = props.Where(prop => prop.PropertyType == typeof(string)).ToArray();
            FieldInfo[] stringFields = fields.Where(field => field.FieldType == typeof(string)).ToArray();

            members.AddRange(stringProps);
            members.AddRange(stringFields);
            
            cInfo.AddRange(stringProps.Select(sp => new MemberInfoAccess
            {
                Accessor = ex == null
                    ? Expression.Property(Expression.Parameter(typeof(T), typeof(T).Name), sp)
                    : Expression.Property(ex, sp),
                Member = sp
            }));
            cInfo.AddRange(stringFields.Select(sp => new MemberInfoAccess
            {
                Accessor = ex == null
                    ? Expression.Field(Expression.Parameter(typeof(T), typeof(T).Name), sp)
                    : Expression.Field(ex, sp),
                Member = sp
            }));

            List<MemberInfo> nonPrimitives = new List<MemberInfo>();
            nonPrimitives.AddRange(props.Where(prop => !prop.PropertyType.IsPrimitive));
            nonPrimitives.AddRange(fields.Where(field => !field.FieldType.IsPrimitive));
            nonPrimitives = nonPrimitives.Except(members).ToList();

            foreach (MemberInfo nonPrimitive in nonPrimitives)
            {
                MemberExpression expression = null;
                if (nonPrimitive is PropertyInfo prop)
                {
                    expression = ex == null
                        ? Expression.Property(Expression.Parameter(typeof(T), typeof(T).Name), prop)
                        : Expression.Property(ex, prop);
                    cInfo.AddRange(GetStringAccessors(prop.PropertyType, expression));
                }
                else if (nonPrimitive is FieldInfo field)
                {
                    expression = ex == null
                        ? Expression.Field(Expression.Parameter(typeof(T), typeof(T).Name), field)
                        : Expression.Field(ex, field);
                    
                    cInfo.AddRange(GetStringAccessors(field.FieldType, expression));
                }
            }
        }
        catch (Exception e)
        {
            Log.Warn(Owner.Default, "Unable to parse MemberInfo for search purposes", exception: e);
        }
        
        return cInfo.ToArray();
    }
}