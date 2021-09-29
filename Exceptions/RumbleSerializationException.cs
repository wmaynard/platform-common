using System;
using Newtonsoft.Json;

namespace Rumble.Platform.Common.Exceptions
{
	public class RumbleSerializationException : RumbleException
	{
		[JsonProperty(NullValueHandling = NullValueHandling.Include)]
		public string BadObject { get; init; }
		
		public RumbleSerializationException(string message, object badObject) : base(message)
		{
			BadObject = badObject.GetType().Name;
		}
	}
}