using System.Text.Json.Serialization;

namespace Rumble.Platform.Common.Exceptions;

public class FieldNotProvidedException : PlatformException
{
    [JsonInclude]
    public string MissingField { get; set; }

    public FieldNotProvidedException(string fieldName) : base("A required field was not provided.") => MissingField = fieldName;
}