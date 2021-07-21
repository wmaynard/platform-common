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
				// TODO: Build out timestamps, log info, etc.
				return null;
			}
		}
	}
}