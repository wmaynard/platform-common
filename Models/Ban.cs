using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Utilities.JsonTools;

namespace Rumble.Platform.Common.Models;

public class Ban : PlatformDataModel
{
    private string[] _audiences;
    
    [BsonElement(TokenInfo.DB_KEY_PERMISSION_SET), BsonIgnoreIfDefault]
    [JsonPropertyName(TokenInfo.FRIENDLY_KEY_PERMISSION_SET)]
    public int PermissionSet { get; set; }
    
    [BsonElement(TokenInfo.DB_KEY_EXPIRATION), BsonIgnoreIfNull]
    [JsonPropertyName(TokenInfo.FRIENDLY_KEY_EXPIRATION)]
    public long? Expiration { get; set; }
    
    [BsonElement("why"), BsonIgnoreIfNull]
    [JsonPropertyName("reason")]
    public string Reason { get; set; }
    
    [BsonElement("id")]
    [JsonPropertyName("id")]
    public string Id { get; set; }
    
    [BsonIgnore]
    [JsonInclude, JsonPropertyName(TokenInfo.FRIENDLY_KEY_AUDIENCE)]
    public string[] Audience
    {
        get
        {
            if (_audiences != null)
                return _audiences;
            
            string[] output = Enum.GetValues<Audience>()
                .Where(aud => aud.IsFlagOf(PermissionSet))
                .Select(aud => aud.GetDisplayName())
                .ToArray();

            string wildcard = Enums.Audience.All.GetDisplayName();
            if (output.Any(str => str == wildcard))
                output = new [] { wildcard };

            return _audiences = output.ToArray();
        }
    }

    protected override void Validate(out List<string> errors)
    {
        errors = new List<string>();
        
        if (PermissionSet <= 0)
            errors.Add("A valid permission is required for a ban to be effective.");

        Id ??= Guid.NewGuid().ToString();
    }
}