using Rumble.Platform.Common.Enums;

namespace Rumble.Platform.Common.Exceptions;

public class WindowExpiredException : PlatformException
{
    public string Reason { get; init; }

    public WindowExpiredException(string reason) : base("An operation is invalid due to an availability window expiring.", code: ErrorCode.NoLongerValid)
    {
        Reason = reason;
    }
}