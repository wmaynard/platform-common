using Rumble.Platform.Common.Enums;

namespace Rumble.Platform.Common.Exceptions.Mongo;

public class RecordsFoundException : PlatformException
{
    public long Minimum { get; init; }
    public long Maximum { get; init; }
    public long Found { get; init; }
    public string Reason { get; init; }
    
    public RecordsFoundException(long min, long max, long found, string reason = null) : base($"{(found < min ? "Fewer" : "More")} records were found than intended.", code: ErrorCode.MongoUnexpectedFoundCount)
    {
        Minimum = min;
        Maximum = max;
        Found = found;
        Reason = reason;
    }

    public RecordsFoundException(long expected, long found, string reason = null) : this(expected, expected, found, reason) { }
}