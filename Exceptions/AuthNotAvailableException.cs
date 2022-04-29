using System.Text.Json.Serialization;

namespace Rumble.Platform.Common.Exceptions;

public class AuthNotAvailableException : PlatformException
{
	[JsonInclude]
	public string TokenAuthEndpoint { get; set; }
	
	public AuthNotAvailableException(string endpoint) : base("Token validation endpoint unreachable.")
	{
		TokenAuthEndpoint = endpoint;
	}
}