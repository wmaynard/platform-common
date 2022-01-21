using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using Rumble.Platform.Common.Filters;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Interfaces;

namespace Rumble.Platform.Common.Web
{
	public abstract class PlatformMongoService<Model> : PlatformService, IPlatformMongoService where Model : PlatformCollectionDocument
	{
		private readonly string MongoConnection = PlatformEnvironment.Variable("MONGODB_URI");
		private readonly string Database = PlatformEnvironment.Variable("MONGODB_NAME");
		// protected abstract string CollectionName { get; }
		private readonly MongoClient _client;
		protected readonly IMongoDatabase _database;
		protected readonly IMongoCollection<Model> _collection;
		protected HttpContext HttpContext => _httpContextAccessor?.HttpContext;

		private bool UseMongoTransaction => (bool)(HttpContext.Items[PlatformMongoTransactionFilter.KEY_USE_MONGO_TRANSACTION] ?? false);
		protected IClientSessionHandle MongoSession
		{
			get => (IClientSessionHandle)HttpContext.Items[PlatformMongoTransactionFilter.KEY_MONGO_SESSION];
			set => HttpContext.Items[PlatformMongoTransactionFilter.KEY_MONGO_SESSION] = value;
		}
		private readonly HttpContextAccessor _httpContextAccessor; 
		
		protected bool IsConnected => _client.Cluster.Description.State == ClusterState.Connected;
		public bool IsHealthy => IsConnected || Open();

		public override object HealthCheckResponseObject => new GenericData() { [GetType().Name] = $"{(IsHealthy ? "" : "dis")}connected" };

		protected PlatformMongoService(string collection)
		{
			Log.Local(Owner.Default, $"Creating {GetType().Name}");
			_client = new MongoClient(MongoConnection);
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

		protected void StartTransactionIfRequested(out IClientSessionHandle session)
		{
			session = MongoSession;

			// Return if the session has already started or if we don't need to use one.
			if (session != null || !UseMongoTransaction)
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
		public void Update(Model model)
		{
			StartTransactionIfRequested(out IClientSessionHandle session);
			if (session != null)
				_collection.ReplaceOne(session, filter: m => model.Id == m.Id, replacement: model);
			else
				_collection.ReplaceOne(filter: m => model.Id == m.Id, replacement: model);
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

		public void InitializeCollection()
		{
			string name = _collection.CollectionNamespace.CollectionName;
			try
			{
				bool exists = _database.ListCollectionsAsync(new ListCollectionsOptions()
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
	}
}