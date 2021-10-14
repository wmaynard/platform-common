using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;

namespace Rumble.Platform.Common.Web
{
	public abstract class PlatformCollectionDocument : PlatformDataModel
	{
		[BsonId, BsonRepresentation(BsonType.ObjectId)]
		[JsonProperty]
		public string Id { get; protected set; }
	}
}