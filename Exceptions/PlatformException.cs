using System;
using System.Text.Json.Serialization;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Exceptions
{
	public class PlatformException : Exception // TODO: Should probably be an abstract class
	{
		[JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string Endpoint { get; private set; }
		
		[JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public ErrorCode Code { get; private set; }
		
		public PlatformException() : this("No message provided."){}
		public PlatformException(string message, Exception inner = null, ErrorCode code = ErrorCode.NotSpecified) : base(message, inner)
		{
			Endpoint = Diagnostics.FindEndpoint();
			Code = code;
		}

		public string Detail
		{
			get
			{
				if (InnerException == null)
					return null;
				string output = "";
				string separator = " | ";
				
				Exception inner = InnerException;
				do
				{
					output += $"({inner.GetType().Name}) {inner.Message}{separator}";
				} while ((inner = inner.InnerException) != null);
				
				output = output[..^separator.Length];
				return output;
			}
		}
	}
}