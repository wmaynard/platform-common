namespace Rumble.Platform.Common.Interfaces
{
	public interface IPlatformMongoService
	{
		public void InitializeCollection();
		public void CreateIndexes();
	}
}