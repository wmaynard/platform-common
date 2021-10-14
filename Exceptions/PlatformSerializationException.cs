using System;
using Newtonsoft.Json;

namespace Rumble.Platform.Common.Exceptions
{
	public class PlatformSerializationException : PlatformException
	{
		[JsonProperty(NullValueHandling = NullValueHandling.Include)]
		public string BadObject { get; init; }
		
		public PlatformSerializationException(string message, object badObject) : base(message)
		{
			BadObject = badObject.GetType().Name;
		}
	}
}