using System;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Exceptions
{
	public class InvalidTokenException : RumbleException
	{
		[JsonProperty(NullValueHandling = NullValueHandling.Include)]
		public string Token { get; private set; }
		
		public InvalidTokenException(string token, Exception inner = null) : base($"Token is invalid.", inner)
		{
			Token = token?.Replace("Bearer ", "");
		}
	}
}