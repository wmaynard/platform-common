using System;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Models;

public class MongoIndexModel : PlatformDataModel
{
    [BsonElement("v")]
    [JsonPropertyName("v")]
    public int Version { get; set; }
    
    [BsonElement("name")]
    [JsonPropertyName("name")]
    public string Name { get; set; }
    
    [BsonElement("ns")]
    [JsonPropertyName("ns")]
    public string Namespace { get; set; }

    [BsonElement("key")]
    [JsonPropertyName("key")]
    public RumbleJson KeyInformation { get; set; }

    public bool IsText => KeyInformation?.Optional<string>("_fts") == "text";
    public bool IsSimple => KeyInformation?.Optional<string>("_fts") != "text";

    internal static MongoIndexModel[] FromCollection<T>(IMongoCollection<T> collection)
    {
        try
        {
            return ((RumbleJson)$"{{\"data\":{collection.Indexes.List().ToList().ToJson()}}}").Require<MongoIndexModel[]>("data");
        }
        catch
        {
            return Array.Empty<MongoIndexModel>();
        }
    }
}