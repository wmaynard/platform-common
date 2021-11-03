using System;
using System.Text.Json.Serialization;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Exceptions
{
	public abstract class PlatformException : Exception // TODO: Should probably be an abstract class
	{
		[JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string Endpoint { get; private set; }
		
		public PlatformException() : this("No message provided."){}
		public PlatformException(string message, Exception inner = null) : base(message, inner)
		{
			Endpoint = Diagnostics.FindEndpoint();
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