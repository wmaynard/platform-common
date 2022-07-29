using System.Text.Json.Serialization;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Exceptions;

public class MissingJsonKeyException : PlatformException
{
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GenericData JSON { get; init; }
    
    [JsonInclude]
    public string MissingKey { get; init; }

    public MissingJsonKeyException(string key) : base($"JSON did not contain required field '{key}'.") => MissingKey = key;

    public MissingJsonKeyException(GenericData json, string key) : this(key) => JSON = json;
}