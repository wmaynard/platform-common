using System;
using Newtonsoft.Json;
using Rumble.Platform.Common.Exceptions;

namespace Rumble.Platform.Common.Web
{
	/// <summary>
	/// This class is used whenever an API call encounters an error.  Its purpose is to limit the amount of information
	/// that is returned to the client and provide uniform JSON responses.
	/// </summary>
	public class ErrorResponse : StandardResponse
	{
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public string Message { get; set; }

		public ErrorResponse(string message, Exception data) : base(data)
		{
			Success = false;
			Message = message;
		}
	}
}