namespace Rumble.Platform.Common.Exceptions;

public class TokenGenerationException : PlatformException
{
    public string Reason { get; init; }
    
    public TokenGenerationException(string reason) : base($"Token generation failed: {reason}")
    {
        Reason = reason;
    }
}