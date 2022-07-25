using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Web;

/// <summary>
/// This class is used whenever an API call encounters an error.  Its purpose is to limit the amount of information
/// that is returned to the client and provide uniform JSON responses.
/// </summary>
internal class ErrorResponse : StandardResponse
{
	[JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Message { get; set; }
	
	[JsonInclude, JsonPropertyName("errorCode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Code { get; set; }

	internal ErrorResponse(string message, Exception data, ErrorCode code) : base(new { Exception = Clean(data) })
	{
		Success = false;
		Message = message;
		Code = $"PLATF-{((int)code).ToString().PadLeft(4, '0')}: {code.ToString()}";
	}

	// Will | 2021.11.05
	// With a change to System.Text.Json, we need to control for Exceptions' tendency to have circular references.
	// After the switch from Newtonsoft, ErrorResponses yielded HTTP 500s because it couldn't serialize the output,
	// even with a converter to handle the exceptions.  By converting Exceptions to a dictionary and limiting
	// the number of number of InnerExceptions, we can prevent this from happening.
	private static Dictionary<string, object> Clean(Exception ex, int depth = 5)
	{
		if (ex == null)
			return null;
		
		Dictionary<string, object> output = new Dictionary<string, object>();
		if (ex is PlatformException platEx)
			output["details"] = platEx.Data;
		output["message"] = ex.Message;
		output["type"] = ex.GetType().Name;
		output["stackTrace"] = ex.StackTrace;
		if (ex.InnerException != null && depth > 0)
			output["innerException"] = Clean(ex.InnerException, depth - 1);
		return output;
	}
}