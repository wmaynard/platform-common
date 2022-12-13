using Rumble.Platform.Common.Enums;

namespace Rumble.Platform.Common.Exceptions;

public class InvalidFieldException : PlatformException
{
    public string Reason { get; init; }

    public InvalidFieldException(string key, string reason) : base($"Invalid request field: {key}.", code: ErrorCode.InvalidRequestData)
    {
        Reason = reason;
    }
}