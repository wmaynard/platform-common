using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Web
{
	public abstract class PlatformMongoService<Model> where Model : PlatformCollectionDocument
	{
		private static readonly string MongoConnection = PlatformEnvironment.Variable("MONGODB_URI");
		private static readonly string Database = PlatformEnvironment.Variable("MONGODB_NAME");
		// protected abstract string CollectionName { get; }
		protected readonly MongoClient _client;
		protected readonly IMongoDatabase _database;
		protected IMongoCollection<Model> _collection;
		
		
		protected bool IsConnected => _client.Cluster.Description.State == ClusterState.Connected;
		public bool IsHealthy => IsConnected || Open();

		public object HealthCheckResponseObject
		{
			get
			{
				IDictionary<string, object> result = new ExpandoObject();
				result[GetType().Name] = $"{(IsHealthy ? "" : "dis")}connected";
				return result;
			}
		}

		protected PlatformMongoService(string collection)
		{
			Log.Local(Owner.Default, $"Creating {GetType().Name}");
			_client = new MongoClient(MongoConnection);
			_database = _client.GetDatabase(Database);
			_collection = _database.GetCollection<Model>(collection);
		}
		
		/// <summary>
		/// Attempts to open the connection to the database by pinging it.
		/// </summary>
		/// <returns>True if the ping is successful and the connection state is open.</returns>
		public bool Open()
		{
			try
			{
				_database.RunCommandAsync((Command<BsonDocument>) "{ping:1}").Wait(); // TODO: This evidently isn't working as expected.  When we had issues with a mongo connection string, this wasn't catching it.
			}
			catch
			{
				return false;
			}

			return IsConnected;
		}

		public virtual IEnumerable<Model> List() => _collection.Find(filter: model => true).ToList();
		public virtual void Create(Model model) => _collection.InsertOne(document: model);
		public virtual void Delete(string id) => _collection.DeleteOne(filter: model => model.Id == id);

		public virtual void Delete(Model model) => Delete(model.Id);
		public virtual void Update(Model model) => _collection.ReplaceOne(filter: m => model.Id == m.Id, replacement: model);
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

		public virtual void DeleteAll()
		{
#if DEBUG
			_collection.DeleteMany(filter: model => true);
			Log.Local(Owner.Default, "All documents deleted.");
#else
			Log.Error(Owner.Default, "Deleting all documents in a collection is not supported outside of local / debug environments.", data: new
			{
				Details = "If this call truly is intended, you need to override the DeleteAll method in your service.",
				Service = GetType().FullName
			});
#endif
		}
	}
}