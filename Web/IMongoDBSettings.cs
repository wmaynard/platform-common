namespace Rumble.Platform.Common.Web
{
	public interface IMongoDBSettings
	{
		public string CollectionName { get; set; }
		public string ConnectionString { get; set; }
		public string DatabaseName { get; set;  }
	}
}