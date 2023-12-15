using Rumble.Platform.Common.Enums;

namespace Rumble.Platform.Common.Exceptions;

public class TooManyRequestsException : PlatformException
{
    public string AccountId { get; init; }
    public int RequestCount { get; init; }
    public long PeriodInSeconds { get; init; }
    public long SecondsRemaining { get; init; }

    public TooManyRequestsException(string accountId, int requestCount, long periodInSeconds, long secondsRemaining) : base("A user has sent too many requests and cannot access the service temporarily.", code: ErrorCode.TooManyRequests)
    {
        AccountId = accountId;
        RequestCount = requestCount;
        PeriodInSeconds = periodInSeconds;
        SecondsRemaining = secondsRemaining;
    }
}