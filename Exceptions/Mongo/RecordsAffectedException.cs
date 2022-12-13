using Rumble.Platform.Common.Enums;

namespace Rumble.Platform.Common.Exceptions.Mongo;

public class RecordsAffectedException : PlatformException
{
    public long Minimum { get; init; }
    public long Maximum { get; init; }
    public long Affected { get; init; }
    
    public RecordsAffectedException(long min, long max, long affected) : base($"{(affected < min ? "Fewer" : "More")} records were found than intended.", code: ErrorCode.MongoUnexpectedAffectedCount)
    {
        Minimum = min;
        Maximum = max;
        Affected = affected;
    }

    public RecordsAffectedException(long expected, long affected) : this(expected, expected, affected) { }
}