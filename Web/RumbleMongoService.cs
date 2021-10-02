using System.Collections.Generic;
using System.Dynamic;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;

namespace Rumble.Platform.Common.Web
{
	public abstract class RumbleMongoService
	{
		protected readonly MongoClient _client;
		protected readonly IMongoDatabase _database;
		protected readonly IMongoCollection<dynamic> _collection;
		
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

		protected RumbleMongoService(MongoDBSettings settings)
		{
			_client = new MongoClient(settings.ConnectionString);
			_database = _client.GetDatabase(settings.DatabaseName);
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
	}
}