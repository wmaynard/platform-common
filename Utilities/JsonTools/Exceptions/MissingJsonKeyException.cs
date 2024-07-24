using System;
using System.Text.Json.Serialization;

namespace Rumble.Platform.Common.Utilities.JsonTools.Exceptions;

public class MissingJsonKeyException : Exception
{
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RumbleJson JSON { get; init; }
    
    [JsonInclude]
    public string MissingKey { get; init; }

    public MissingJsonKeyException(string key) : base($"JSON did not contain required field '{key}'.") => MissingKey = key;

    public MissingJsonKeyException(RumbleJson json, string key) : this(key) => JSON = json;
}