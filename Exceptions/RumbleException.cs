using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Exceptions
{
	public class RumbleException : Exception
	{
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public string Endpoint { get; private set; }
		
		public RumbleException() : this("No message provided."){}
		public RumbleException(string message, Exception inner = null) : base(message, inner)
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