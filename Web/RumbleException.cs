using System;

namespace Rumble.Platform.Common.Web
{
	public class RumbleException : Exception
	{
		public RumbleException() : this("No message provided."){}
		public RumbleException(string message) : base(message){}
		public RumbleException(string message, Exception inner) : base(message, inner){}

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