using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Utilities.JsonTools;

namespace Rumble.Platform.Common.Exceptions.Mongo;

public class RecordNotFoundException : PlatformException
{
    public string Collection { get; init; }
    public new RumbleJson Data { get; init; }

    public RecordNotFoundException(string collection, string message, RumbleJson data = null) : base($"No record found: {message}", code: ErrorCode.MongoRecordNotFound)
    {
        Collection = collection;
        Data = data;
    }
}