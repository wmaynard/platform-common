using System;
using System.Runtime.Serialization;

namespace Platform.CSharp.Common.Web
{
	public class InvalidTokenException : Exception
	{
		public InvalidTokenException() : this("Token is invalid."){}
		public InvalidTokenException(SerializationInfo info, StreamingContext context) : base(info, context){}
		public InvalidTokenException(string message) : base(message){}
		public InvalidTokenException(string message, Exception inner) : base(message, inner) {}
	}
}