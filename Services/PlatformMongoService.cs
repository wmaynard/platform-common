using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
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
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Services;

public abstract class PlatformMongoService<Model> : PlatformService, IPlatformMongoService where Model : PlatformCollectionDocument
{
    private string Connection { get; init; }
    private string Database { get; init; }
    private readonly MongoClient _client;
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

    protected PlatformMongoService(string collection)
    {
        Connection = PlatformEnvironment.MongoConnectionString;
        Database = PlatformEnvironment.MongoDatabaseName;

        if (string.IsNullOrEmpty(Connection))
            Log.Error(Owner.Default, $"Missing Mongo-related environment variable '{PlatformEnvironment.KEY_MONGODB_URI}'.");
        if (string.IsNullOrEmpty(Database))
            Log.Error(Owner.Default, $"Missing Mongo-related environment variable '{PlatformEnvironment.KEY_MONGODB_NAME}'.");

        _client = new MongoClient(Connection);
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

    public void CreateIndexes()
    {
        List<BsonDocument> docs = _collection.Indexes.List().ToList();
        MongoIndex[] dbIndexes = docs.Select(doc => new MongoIndex
        {
            IndexName = doc.GetElement("name").Value.ToString(),
            DatabaseKey = ((BsonDocument)doc.GetElement("key").Value).Elements.FirstOrDefault().Name
        }).ToArray();

        SimpleIndex[] modelIndexes = typeof(Model)
            .GetProperties()
            .Select(property => ((SimpleIndex)property
                .GetCustomAttributes()
                .FirstOrDefault(attribute => attribute is SimpleIndex))
                ?.SetPropertyName(property.Name)
            )
            .Where(attribute => attribute != null)
            .Select(attribute => (SimpleIndex)attribute)
            .ToArray();

        if (!modelIndexes.Any())
            return;

        foreach (SimpleIndex modelIndex in modelIndexes)
        {
            string name = $"{_database.DatabaseNamespace.DatabaseName}.{_collection.CollectionNamespace.CollectionName}.{modelIndex.DatabaseKey}";
            MongoIndex dbIndex = dbIndexes.FirstOrDefault(index => index.DatabaseKey == modelIndex.DatabaseKey);
            if (dbIndex == null)
            {
                Log.Info(Owner.Will, $"Creating index '{modelIndex.Name}' on '{name}'.");
                #pragma warning disable CS0618
                _collection.Indexes.CreateOne(Builders<Model>.IndexKeys.Ascending(modelIndex.DatabaseKey), new CreateIndexOptions()
                #pragma warning restore CS0618
                {
                    Name = modelIndex.Name,
                    Unique = modelIndex.Unique
                });
            }
            else if (dbIndex.IndexName != modelIndex.Name)
                Log.Verbose(Owner.Will, $"An index already exists on '{name}' ({dbIndex.IndexName}).  Manually assigned indexes have priority, so the model's is ignored.");
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

    public override GenericData HealthStatus => new GenericData
    {
        { Name, IsHealthy ? "connected" : "disconnected" }
    };
}