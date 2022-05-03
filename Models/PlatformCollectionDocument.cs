using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Rumble.Platform.Common.Models;

public abstract class PlatformCollectionDocument : PlatformDataModel
{
	[BsonId, BsonRepresentation(BsonType.ObjectId)]
	[JsonInclude]
	public string Id { get; protected set; }

	public void ChangeId() => Id = ObjectId.GenerateNewId().ToString();
	public void NullifyId() => Id = null;
}