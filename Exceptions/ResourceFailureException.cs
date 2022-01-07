using System;

namespace Rumble.Platform.Common.Exceptions
{
	public class ResourceFailureException : PlatformException
	{
		public string Detail { get; set; }
		
		public ResourceFailureException(string detail, Exception exception = null) : base("A ResourceFilter failed to load some data.", exception)
		{
			Detail = detail;
		}
	}
}