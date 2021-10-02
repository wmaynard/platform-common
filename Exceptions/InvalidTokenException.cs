using System;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Exceptions
{
	public class InvalidTokenException : RumbleException
	{
		[JsonProperty(NullValueHandling = NullValueHandling.Include)]
		public string EncryptedToken { get; private set; }
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public TokenInfo Token { get; private set; }
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
		public bool EmptyToken { get; private set; }
		[JsonProperty(NullValueHandling = NullValueHandling.Include)]
		public string VerificationEndpoint { get; private set; }
		public InvalidTokenException(string token, string endpoint, Exception inner = null) : base("Token is invalid.", inner)
		{
			EncryptedToken = token?.Replace("Bearer ", "");
			EmptyToken = string.IsNullOrEmpty(token);
			VerificationEndpoint = endpoint;
		}

		public InvalidTokenException(string token, TokenInfo info, string endpoint, Exception inner = null) : this(token, endpoint, inner)
		{
			Token = info;
		}
	}
}