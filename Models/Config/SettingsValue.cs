using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace Rumble.Platform.Common.Models.Config;

public class SettingsValue : PlatformDataModel
{
    [BsonElement("value"), BsonIgnoreIfNull]
    [JsonInclude, JsonPropertyName("value")]
    public object Value { get; init; }
    
    [BsonElement("comment"), BsonIgnoreIfNull]
    [JsonInclude, JsonPropertyName("comment")]
    public string Comment { get; init; }

    [BsonConstructor, JsonConstructor]
    public SettingsValue(object value = null, string comment = null)
    {
        Value = value;
        Comment = comment;
    }
}