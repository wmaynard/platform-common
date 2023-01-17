using Rumble.Platform.Common.Enums;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Exceptions.Mongo;

public class RecordNotFoundException : PlatformException
{
    public string Collection { get; init; }
    public RumbleJson Data { get; init; }

    public RecordNotFoundException(string collection, string message, RumbleJson data = null) : base($"No record found: {message}", code: ErrorCode.MongoRecordNotFound)
    {
        Collection = collection;
        Data = data;
    }
}