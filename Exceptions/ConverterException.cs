using System;
using System.Text.Json.Serialization;

namespace Rumble.Platform.Common.Exceptions
{
	public class ConverterException : PlatformException
	{
		[JsonInclude]
		public string Info { get; init; }
		public ConverterException(string message, Type attemptedType, Exception inner = null, bool onDeserialize = false)
			: base($"Unable to {(onDeserialize ? "de" : "")}serialize {attemptedType.Name}.", inner)
		{
			Info = message;
		}
	}
}