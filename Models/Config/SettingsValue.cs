using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Models.Config;

[BsonIgnoreExtraElements]
public class SettingsValue : PlatformDataModel
{
    [BsonElement("value"), BsonIgnoreIfNull]
    [JsonInclude, JsonPropertyName("value")]
    public object Value { get; init; }
    
    [BsonElement("comment"), BsonIgnoreIfNull]
    [JsonInclude, JsonPropertyName("comment")]
    public string Comment { get; set; }

    [BsonConstructor, JsonConstructor]
    public SettingsValue(object value = null, string comment = null)
    {
        Value = value;
        Comment = comment;
    }
}