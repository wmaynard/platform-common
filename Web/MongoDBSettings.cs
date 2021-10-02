namespace Rumble.Platform.Common.Web
{
	public abstract class MongoDBSettings
	{
		public string CollectionName { get; set; }
		public string ConnectionString { get; set; }
		public string DatabaseName { get; set;  }
	}
}