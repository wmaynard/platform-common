using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Rumble.Platform.Common.Web
{
	public abstract class PlatformCollectionDocument : PlatformDataModel
	{
		[BsonId, BsonRepresentation(BsonType.ObjectId)]
		[JsonInclude]
		public string Id { get; protected set; }
	}
}