using Newtonsoft.Json;

namespace Rumble.Platform.Common.Exceptions
{
	public class AuthNotAvailableException : PlatformException
	{
		[JsonProperty(NullValueHandling = NullValueHandling.Include)]
		public string TokenAuthEndpoint { get; set; }
		
		public AuthNotAvailableException(string endpoint) : base("Token validation endpoint unreachable.")
		{
			TokenAuthEndpoint = endpoint;
		}
	}
}