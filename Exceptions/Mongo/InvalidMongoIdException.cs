using Rumble.Platform.Common.Enums;

namespace Rumble.Platform.Common.Exceptions.Mongo;

public class InvalidMongoIdException : PlatformException
{
    public string Value { get; init; }
    public InvalidMongoIdException(string value) : base("A string was passed in that was supposed to be a MongoDB ID, but was not a 24-digit hex string.", code: ErrorCode.InvalidDataType)
    {
        Value = value;
    }
}