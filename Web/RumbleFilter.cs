using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using platform_CSharp_library.Web;
using Platform.CSharp.Common.Web;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.ChatService.Utilities
{
	/// <summary>
	/// This class is designed to catch Exceptions that the API throws.  Our API should not be dumping stack traces
	/// or other potentially security-compromising data to a client outside of our debug environment.  In order to
	/// prevent that, we need to have a catch-all implementation for Exceptions in OnActionExecuted.
	/// </summary>
	public class RumbleFilter : IActionFilter
	{
		public RumbleFilter(){}
		
		public void OnActionExecuting(ActionExecutingContext context){}

		/// <summary>
		/// This triggers after an action executes, but before any uncaught Exceptions are dealt with.  Here we can
		/// make sure we prevent stack traces from going out and return a BadRequestResult instead (for example).
		/// Dumping too much information out to bad requests is unnecessary risk for bad actors.
		/// </summary>
		/// <param name="context"></param>
		public void OnActionExecuted(ActionExecutedContext context)
		{
			
			// {
			// 	"success": false,
			// 	"errorCode": "authentication",
			// 	"debugText": "authentication"
			// }
			if (context.Exception == null)
				return;

			Exception ex = context.Exception?.InnerException ?? context.Exception;

			string code = ex switch
			{
				JsonSerializationException => "Invalid JSON.",
				ArgumentNullException => ex.Message,
				RumbleException => ex.Message,
				BadHttpRequestException => ex.Message,
				_ => $"Unhandled or unexpected exception. ({ex.GetType().Name})" // TODO: Log these as warnings
			};
			string debug = ex switch
			{
				RumbleException exception => exception.Detail,
				_ => "(No information provided)"
			};

			context.Result = new BadRequestObjectResult(new ErrorResponse(
				errorCode: code,
				debugText: debug
			));
			context.ExceptionHandled = true;
		}
	}
}