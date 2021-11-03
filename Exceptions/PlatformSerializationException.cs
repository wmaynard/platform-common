using System;
using System.Text.Json.Serialization;

namespace Rumble.Platform.Common.Exceptions
{
	public class PlatformSerializationException : PlatformException
	{
		[JsonInclude]
		public string BadObject { get; init; }
		
		public PlatformSerializationException(string message, object badObject) : base(message)
		{
			BadObject = badObject.GetType().Name;
		}
	}
}