using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Common.Interop;

// TODO: Review all endpoints for appropriate HTTP methods (e.g. DELETE, PUT)
namespace Rumble.Platform.Common.Filters
{
	
	/// <summary>
	/// This class is designed to catch Exceptions that the API throws.  Our API should not be dumping stack traces
	/// or other potentially security-compromising data to a client outside of our debug environment.  In order to
	/// prevent that, we need to have a catch-all implementation for Exceptions in OnActionExecuted.
	/// </summary>
	[SuppressMessage("ReSharper", "ConditionIsAlwaysTrueOrFalse")]
	public class PlatformExceptionFilter : PlatformBaseFilter, IExceptionFilter
	{
		/// <summary>
		/// This triggers after an action executes, but before any uncaught Exceptions are dealt with.  Here we can
		/// make sure we prevent stack traces from going out and return a BadRequestResult instead (for example).
		/// Dumping too much information out to bad requests is unnecessary risk for bad actors.
		/// </summary>
		/// <param name="context"></param>
		public void OnException(ExceptionContext context)
		{
			if (context.Exception == null)
				return;

			Exception ex = context.Exception;

			string message = ex switch
			{
				// JsonSerializationException => "Invalid JSON.",
				JsonException => "Invalid JSON.",
				ArgumentNullException => ex.Message,
				PlatformException => ex.Message,
				BadHttpRequestException => ex.Message,
				_ => $"Unhandled or unexpected exception. ({ex.GetType().Name})"
			};
			ErrorCode code = ErrorCode.NotSpecified;
			if (ex is PlatformException)
				code = ((PlatformException)ex).Code;

			// Special handling for MongoCommandException because it doesn't like being serialized to JSON.
			if (ex is MongoCommandException mce)
			{
				ex = new PlatformMongoException(mce);
				Log.Critical(Owner.Default, "Something went wrong with MongoDB.", data: Converter.ContextToEndpointObject(context), exception: mce);
			}
			else
				Log.Error(Owner.Default, message: $"{ex.GetType().Name}: {message}", data: Converter.ContextToEndpointObject(context), exception: ex);

			context.Result = new BadRequestObjectResult(new ErrorResponse(
				message: message,
				data: ex,
				code: code
			));
			context.ExceptionHandled = true;
			Graphite.Track(Graphite.KEY_EXCEPTION_COUNT, 1, Converter.ContextToEndpoint(context), Graphite.Metrics.Type.FLAT);
		}
	}
}