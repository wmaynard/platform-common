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
using RCL.Logging;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Common.Interop;

namespace Rumble.Platform.Common.Filters;

/// <summary>
/// This class is designed to catch Exceptions that the API throws.  Our API should not be dumping stack traces
/// or other potentially security-compromising data to a client outside of our debug environment.  In order to
/// prevent that, we need to have a catch-all implementation for Exceptions in OnActionExecuted.
/// </summary>
[SuppressMessage("ReSharper", "ConditionIsAlwaysTrueOrFalse")]
public class PlatformExceptionFilter : PlatformFilter, IExceptionFilter
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
        GenericData data = new GenericData();
        string origin = (string)context.HttpContext.Items[PlatformResourceFilter.KEY_REQUEST_ORIGIN];

        string message = ex switch
        {
            // JsonSerializationException => "Invalid JSON.",
            JsonException => "Invalid JSON.",
            ArgumentNullException => ex.Message,
            PlatformException => ex.Message,
            BadHttpRequestException => ex.Message,
            _ => $"Unhandled or unexpected exception. ({ex.GetType().Name})"
        };
        ErrorCode code = ErrorCode.RuntimeException;
        if (ex is PlatformException platEx)
        {
            code = platEx.Code;
            data = platEx.Data;
        }

        data["endpoint"] = context.GetEndpoint();
        data["origin"] = origin;
        data["baseUrl"] = PlatformEnvironment.Url("/");
        data["environment"] = PlatformEnvironment.Name;

        // Special handling for MongoCommandException because it doesn't like being serialized to JSON.
        if (ex is MongoCommandException mce)
        {
            ex = new PlatformMongoException(mce);
            Log.Critical(Owner.Default, "Something went wrong with MongoDB.", data: data, exception: mce);
        }
        else
            Log.Error(Owner.Default, message: $"{ex.GetType().Name}: {message}", data: data, exception: ex);

        context.Result = new BadRequestObjectResult(new ErrorResponse(
            message: message,
            data: ex,
            code: code
        ));
        context.ExceptionHandled = true;
        Graphite.Track(Graphite.KEY_EXCEPTION_COUNT, 1, context.GetEndpoint(), Graphite.Metrics.Type.FLAT);
    }
}