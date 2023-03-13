using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using RCL.Logging;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Filters;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Interfaces;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Services;

public abstract class PlatformMongoService<Model> : PlatformService, IPlatformMongoService where Model : PlatformCollectionDocument
{
    public const int DEFAULT_MONGO_MAX_POOL_CONNECTION_SIZE = 100; // 100 is from the docs
    
    private string Connection { get; init; }
    private string Database { get; init; }
    
    private static MongoClient _client;
    public static  int         MaxMongoConnections = DEFAULT_MONGO_MAX_POOL_CONNECTION_SIZE;
    
    protected readonly IMongoDatabase _database;
    protected readonly IMongoCollection<Model> _collection;
    protected HttpContext HttpContext => _httpContextAccessor?.HttpContext;

    private bool UseMongoTransaction => (bool)(HttpContext?.Items[PlatformMongoTransactionFilter.KEY_USE_MONGO_TRANSACTION] ?? false);
    protected IClientSessionHandle MongoSession
    {
        get => (IClientSessionHandle)HttpContext?.Items[PlatformMongoTransactionFilter.KEY_MONGO_SESSION];
        private set => HttpContext.Items[PlatformMongoTransactionFilter.KEY_MONGO_SESSION] = value;
    }
    private readonly HttpContextAccessor _httpContextAccessor; 

    public bool IsConnected => _client.Cluster.Description.State == ClusterState.Connected;
    public bool IsHealthy => IsConnected || Open();
    public string CollectionName => _collection?.CollectionNamespace?.CollectionName;

    protected PlatformMongoService(string collection)
    {
        Connection = PlatformEnvironment.MongoConnectionString;
        Database = PlatformEnvironment.MongoDatabaseName;

        if (string.IsNullOrEmpty(Connection))
            Log.Error(Owner.Default, $"Missing Mongo-related environment variable '{PlatformEnvironment.KEY_MONGODB_URI}'.");
        if (string.IsNullOrEmpty(Database))
            Log.Error(Owner.Default, $"Missing Mongo-related environment variable '{PlatformEnvironment.KEY_MONGODB_NAME}'.");

        if (_client == null)
        {
            var settings = MongoClientSettings.FromConnectionString(Connection);
            settings.MaxConnectionPoolSize = MaxMongoConnections;

            _client = new MongoClient(settings);
        }

        _database = _client.GetDatabase(Database);
        _collection = _database.GetCollection<Model>(collection);
        _httpContextAccessor = new HttpContextAccessor();
    }

    /// <summary>
    /// Attempts to open the connection to the database by pinging it.
    /// </summary>
    /// <returns>True if the ping is successful and the connection state is open.</returns>
    public bool Open()
    {
        // TODO: This evidently isn't working as expected.  When we had issues with a mongo connection string, this wasn't catching it.
        try { _database.RunCommandAsync((Command<BsonDocument>) "{ping:1}").Wait(); }
        catch { return false; }

        return IsConnected;
    }

    public virtual IEnumerable<Model> List() => _collection.Find(filter: model => true).ToList();

    protected void StartTransactionIfRequested(out IClientSessionHandle session) => StartTransaction(out session, attributeOverride: false);
    protected void StartTransaction(out IClientSessionHandle session) => StartTransaction(out session, attributeOverride: true);

    private void StartTransaction(out IClientSessionHandle session, bool attributeOverride)
    {
        session = MongoSession;

        // Return if the session has already started or if we don't need to use one.
        if (session != null || (!attributeOverride && !UseMongoTransaction))
            return;

        Log.Verbose(Owner.Default, "Starting MongoDB transaction.");
        session = _client.StartSession();
        try
        {
            session.StartTransaction();
        }
        catch (NotSupportedException e) 
        {
            // MongoDB Transactions are not supported in non-clustered environments ("standalone servers").  Mark the context field as false so we don't keep retrying.
            // This should not affect deployed code - only local.
            if (!PlatformEnvironment.IsLocal)
                Log.Error(Owner.Default, "Unable to start a MongoDB transaction.", exception: e);
            HttpContext.Items[PlatformMongoTransactionFilter.KEY_USE_MONGO_TRANSACTION] = false;
            return;
        }
        MongoSession = session;
    }

    public void CommitTransaction()
    {
        try
        {
            MongoSession?.CommitTransaction();
            MongoSession = null;
        }
        catch (Exception e)
        {
            Log.Error(Owner.Will, "Could not commit transaction from PlatformMongoService.CommitTransaction().", exception: e);
        }
    }

    public IClientSessionHandle StartTransaction()
    {
        // StartTransactionIfRequested(out IClientSessionHandle session);
        IClientSessionHandle output = _client.StartSession();
        output.StartTransaction();
        return output;
    }

    public void CommitTransaction(IClientSessionHandle session = null)
    {
        session ??= MongoSession;

        if (session == null)
            throw new PlatformException("Unable to commit transaction; session has not started yet.", code: ErrorCode.MongoSessionIsNull);

        session.CommitTransaction();
    }

    public Model Create(Model model)
    {
        StartTransactionIfRequested(out IClientSessionHandle session);
        if (session != null)
            _collection.InsertOne(session, model);
        else
            _collection.InsertOne(model);
        return model;
    }
    public void Delete(string id)
    {
        StartTransactionIfRequested(out IClientSessionHandle session);
        if (session != null)
            _collection.DeleteOne(session, filter: model => model.Id == id);
        else
            _collection.DeleteOne(filter: model => model.Id == id);
    }

    public void Delete(Model model) => Delete(model.Id);
    public void Update(Model model, bool createIfNotFound = false)
    {
        StartTransactionIfRequested(out IClientSessionHandle session);
        if (!createIfNotFound && model.Id == null)
            throw new PlatformException(message: "Model.Id is null; update will be unsuccessful without upsert.");
        ReplaceOptions options = new ReplaceOptions() { IsUpsert = createIfNotFound };
        if (session != null)
            _collection.ReplaceOne(session, filter: m => model.Id == m.Id, replacement: model, options: options);
        else
            _collection.ReplaceOne(filter: m => model.Id == m.Id, replacement: model, options: options);
    }

    public virtual Model[] Find(Expression<Func<Model, bool>> filter) => _collection.Find(filter).ToList().ToArray();
    public virtual Model FindOne(Expression<Func<Model, bool>> filter) => _collection.Find(filter).FirstOrDefault();

    public virtual Model Get(string id)
    {
        Model output = _collection.Find(filter: model => model.Id == id).FirstOrDefault();
        if (output == null)
            Log.Warn(Owner.Default, "The specified document ID does not exist in MongoDB.", data: new
            {
                Id = id,
                Model = typeof(Model).Name,
                Service = GetType().Name,
            });
        return output;
    }

    /// <summary>
    /// Recursively pulls indexes from PlatformCollectionDocuments and PlatformDataModels.
    /// </summary>
    /// <param name="property">The property to draw indexes from.  May not necessarily have indexes.</param>
    /// <param name="parentName">The parent's friendly key, for logging purposes.</param>
    /// <param name="parentDbKey">The parent's database key, required to create indexes.</param>
    /// <param name="depth">The maximum depth to create keys for.</param>
    /// <returns>An array of indexes to create.</returns>
    private PlatformMongoIndex[] ExtractIndexes(PropertyInfo property, string parentName = null, string parentDbKey = null, int depth = 5)
    {
        if (depth <= 0)
        {
            Log.Error(Owner.Default, "Maximum depth exceeded for Mongo indexes.");
            return Array.Empty<PlatformMongoIndex>();
        }

        BsonElementAttribute bson = property.GetCustomAttribute<BsonElementAttribute>();
        string dbName = bson?.ElementName;
        string friendlyName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;

        parentDbKey = string.IsNullOrWhiteSpace(parentDbKey)
            ? dbName
            : $"{parentDbKey}.{dbName}";
        parentName = string.IsNullOrWhiteSpace(parentName)
            ? friendlyName
            : $"{parentName}.{friendlyName}";
        
        List<PlatformMongoIndex> output = property
            .GetCustomAttributes()
            .Where(attribute => attribute.GetType().IsAssignableTo(typeof(PlatformMongoIndex)))
            .Select(attribute => ((PlatformMongoIndex)attribute)
                .SetPropertyName(parentName)
                .SetDatabaseKey(parentDbKey)
            )
            .ToList();
        
        AdditionalIndexKey[] additionalKeys = property
            .GetCustomAttributes()
            .Where(attribute => attribute.GetType().IsAssignableTo(typeof(AdditionalIndexKey)))
            .Select(attribute => (AdditionalIndexKey)attribute)
            .ToArray();

        foreach (CompoundIndex compound in output.OfType<CompoundIndex>())
            compound.AddKeys(additionalKeys.Where(adds => adds.GroupName == compound.GroupName));
        
        if (property.PropertyType.IsAssignableTo(typeof(PlatformDataModel)))
            foreach (PropertyInfo nested in GetIndexCandidates(property.PropertyType))
                output.AddRange(ExtractIndexes(nested, property.Name, parentDbKey, depth - 1));

        if (bson != null)
            return output.ToArray();
        if (output.Any())
            Log.Warn(Owner.Default, "Unable to create indexes without a BsonElement attribute also present on a property.", data: new
            {
                Name = property.Name
            });
        return Array.Empty<PlatformMongoIndex>();
    }

    /// <summary>
    /// Pulls PlatformMongoIndexes out of a PlatformCollectionDocument.
    /// </summary>
    /// <returns>Returns all SimpleIndexes, one CompoundIndex per group name, and a maximum of one TextIndex.</returns>
    private PlatformMongoIndex[] ExtractIndexes()
    {
        List<PlatformMongoIndex> output = new List<PlatformMongoIndex>();
        List<PlatformMongoIndex> indexes = new List<PlatformMongoIndex>();
        List<PropertyInfo> candidates = GetIndexCandidates(typeof(Model)).ToList();
        
        // Perform a deep dive on this service's models.  Look for all generic arguments and nested types that can
        // support index attributes (PlatformDataModels).
        Type type = GetType();
        do
        {
            candidates.AddRange(type
                .GetGenericArguments()
                .Union(type.GetNestedTypes(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                .Where(model => model.IsAssignableTo(typeof(PlatformDataModel)))
                .SelectMany(GetIndexCandidates)
            );
            
            type = type.BaseType;
        } while (type?.IsAssignableTo(typeof(IPlatformMongoService)) ?? false);

        // Pull out all the indexes defined by the attributes found on the properties.
        foreach (PropertyInfo property in candidates)
            indexes.AddRange(ExtractIndexes(property));

        // We can't add all of the indexes on their own.  Limitations:
        //     A collection can only support one text index, so we have to combine them.
        //     Our compound indexes have not yet been combined into a comprehensive definition.
        TextIndex[] texts = indexes.OfType<TextIndex>().ToArray();
        CompoundIndex[] compounds = indexes.OfType<CompoundIndex>().ToArray();
        SimpleIndex[] simples = indexes.OfType<SimpleIndex>().ToArray();
        
        output.AddRange(simples);

        // Combine text indexes into one definition.
        if (texts.Any())
            output.Add(new TextIndex
            {
                Name = "text",
                DatabaseKeys = texts
                    .Select(text => text.DatabaseKey)
                    .ToArray()
            });
        // Combine compound indexes into one definition - grouped by their name.
        if (compounds.Any()) 
            output.AddRange(compounds
                .Select(compound => compound.GroupName)
                .Distinct()
                .Select(group => CompoundIndex.Combine(compounds
                    .Where(compound => compound.GroupName == group)
                    .ToArray())
                )
            );
        
        return output.ToArray();
    }
    
    /// <summary>
    /// Limits properties that can have indexes on them.  Properties must be public.  BsonIgnore attributes also disqualify properties;
    /// otherwise, infinite recursion is possible.
    /// </summary>
    /// <param name="type">The data model or collection document to get indexes from.</param>
    /// <returns>An array of property reflection data.</returns>
    internal virtual PropertyInfo[] GetIndexCandidates(Type type) => type
        .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
        .Where(prop => !prop.GetCustomAttributes().Any(att => att.GetType().IsAssignableTo(typeof(BsonIgnoreAttribute))))
        .ToArray();

    public void CreateIndexes()
    {
        PlatformMongoIndex[] indexes = ExtractIndexes();
        if (!indexes.Any())
            return;

        MongoIndexModel[] dbIndexes = MongoIndexModel.FromCollection(_collection);

        foreach (PlatformMongoIndex index in indexes)
        {
            CreateIndexModel<Model> model = null;
            bool drop = false;
            
            switch (index)
            {
                case CompoundIndex compound:
                    MongoIndexModel existingCompound = dbIndexes.FirstOrDefault(dbIndex => dbIndex.IsCompound && dbIndex.Name == compound.GroupName);
                    if (existingCompound != null)
                    {
                        string[] keys = compound.Keys;
                        string[] existingKeys = existingCompound.KeyInformation.Select(pair => pair.Key).ToArray();

                        drop = keys.Except(existingKeys).Any() || existingKeys.Except(keys).Any();
                        if (!drop)
                            continue;
                        index.Name = existingCompound.Name;
                    }

                    model = new CreateIndexModel<Model>(
                        keys: compound.BuildKeysDefinition<Model>(),
                        options: new CreateIndexOptions<Model>
                        {
                            Name = compound.GroupName,
                            Background = true
                        }
                    );
                    break;
                case SimpleIndex simple:
                    MongoIndexModel existingSimple = dbIndexes.FirstOrDefault(dbIndex => dbIndex.IsSimple && dbIndex.KeyInformation.ContainsKey(index.DatabaseKey));

                    if (existingSimple != null)
                    {
                        drop = existingSimple.KeyInformation.Select(pair => pair.Key).FirstOrDefault() != simple.DatabaseKey 
                               || existingSimple.Unique != simple.Unique;
                        if (!drop)
                            continue;
                        index.Name = existingSimple?.Name;
                    }

                    model = new CreateIndexModel<Model>(
                        keys: simple.Ascending
                            ? Builders<Model>.IndexKeys.Ascending(simple.DatabaseKey)
                            : Builders<Model>.IndexKeys.Descending(simple.DatabaseKey),
                        options: new CreateIndexOptions<Model>
                        {
                            Name = simple.Name,
                            Background = true,
                            Unique = simple.Unique
                        }
                    );
                    break;
                case TextIndex text:
                    if (dbIndexes.Any(model => model.IsText))
                        continue;
                    model = new CreateIndexModel<Model>(
                        keys: Builders<Model>.IndexKeys.Combine(
                            text.DatabaseKeys.Select(dbKey => Builders<Model>.IndexKeys.Text(dbKey))
                        ),
                        options: new CreateIndexOptions<Model>
                        {
                            Name = text.Name,
                            Background = true,
                            Sparse = false
                        }
                    );
                    break;
            }
            if (drop)
                try
                {
                    _collection.Indexes.DropOne(index.Name);
                    Log.Warn(Owner.Will, "Mongo index dropped.  If this is not rare, treat it as an error.", new
                    {
                        Name = index.Name,
                        Collection = _collection.CollectionNamespace
                    });
                }
                catch (MongoCommandException e)
                {
                    Log.Error(Owner.Default, "Unable to drop index.", data: new
                    {
                        Name = index.Name,
                        Collection = _collection.CollectionNamespace
                    }, exception: e);
                }
            try
            {
                if (model != null)
                    _collection.Indexes.CreateOne(model);
            }
            catch (MongoCommandException e)
            {
                Log.Error(Owner.Will, $"Unable to create index.", data: new
                {
                    Name = index.Name,
                    Collection = _collection.CollectionNamespace
                }, exception: e);
                ApiService.Instance?.Alert(
                    title: "Mongo Index Creation Failure",
                    message: "Platform-Common was unable to create a specified index on startup.  MongoDB may have performance problems if this is unaddressed.",
                    countRequired: PlatformEnvironment.IsDev ? 120 : 15,
                    timeframe: 600,
                    owner: Owner.Will,
                    impact: ImpactType.PerformanceNotOptimized
                );
            }
        }
    }

    public void InitializeCollection()
    {
        string name = _collection.CollectionNamespace.CollectionName;
        try
        {
            bool exists = _database.ListCollectionsAsync(new ListCollectionsOptions
            {
                Filter = new BsonDocument("name", name)
            }).Result.Any();

        if (exists)
            return;

        _database.CreateCollection(name);
        Log.Info(Owner.Will, $"Created collection '{name}'");
        }
        catch (Exception e)
        {
            string command = "";
            if (e.InnerException is MongoCommandException)
            {
                string msg = e.InnerException.Message;
                int colon = msg.IndexOf(":", StringComparison.Ordinal);
                if (colon > -1)
                    command = $" ({msg[..colon]})";
            }
            Log.Error(Owner.Will, $"Unable to create collection on '{_database.DatabaseNamespace.DatabaseName}'{command}.  This is likely a permissions issue.", exception: e);
            throw;
        }
    }

    public virtual void DeleteAll()
    {
    #if DEBUG
    StartTransactionIfRequested(out IClientSessionHandle session);
    if (session != null)
        _collection.DeleteMany(session, filter: model => true);
    else
        _collection.DeleteMany(filter: model => true);
    Log.Local(Owner.Default, "All documents deleted.");
    #else
    Log.Error(Owner.Default, "Deleting all documents in a collection is not supported outside of local / debug environments.", data: new
    {
        Details = "If this call truly is intended, you need to override the DeleteAll method in your service and will need to manually control the Mongo transactions (if using them).",
        Service = GetType().FullName
    });
    #endif
    }

    // TODO: We need a mongo wrapper to handle sessions in conjunction with _collection.{METHOD}.
    // Mongo throws exceptions if you try to hand it a null session, which can happen if UseMongoTransaction isn't specified, and
    // transactions don't work on local installs without special setup.  This results in duplicated commands everywhere sessions are optional.

    private class MongoIndex
    {
        internal string IndexName { get; set; }
        internal string DatabaseKey { get; set; }
    }

    public override RumbleJson HealthStatus => new RumbleJson
    {
        { Name, IsHealthy ? "connected" : "disconnected" }
    };
}