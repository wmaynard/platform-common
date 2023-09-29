using Rumble.Platform.Common.Enums;

namespace Rumble.Platform.Common.Exceptions;

public class TokenGenerationException : PlatformException
{
    public string Reason { get; init; }
    
    public TokenGenerationException(string reason) : base($"Token generation failed: {reason}", code: ErrorCode.Locked)
    {
        Reason = reason;
    }
}

public class TokenBannedException : PlatformException
{
    public string Reason { get; init; }
    public long? BannedUntil { get; init; }
    
    public TokenBannedException(string reason, long? bannedUntil = null) : base($"Token generation failed: {reason}", code: ErrorCode.Locked)
    {
        Reason = reason;
        BannedUntil = bannedUntil;
    }
}