using System;
using System.Runtime.Serialization;

namespace Rumble.Platform.Common.Web
{
	public class InvalidTokenException : RumbleException
	{
		public InvalidTokenException() : base("Token is invalid."){}
		public InvalidTokenException(string reason) : base($"Token is invalid. ({reason})"){}
	}
}