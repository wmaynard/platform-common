using System;
using System.Runtime.Serialization;
using platform_CSharp_library.Web;

namespace Platform.CSharp.Common.Web
{
	public class InvalidTokenException : RumbleException
	{
		public InvalidTokenException() : base("Token is invalid."){}
		public InvalidTokenException(string reason) : base($"Token is invalid. ({reason})"){}
	}
}