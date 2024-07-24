using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Common.Interop;

namespace Rumble.Platform.Common.Filters;

    public class PlatformPerformanceFilter : PlatformFilter, IAuthorizationFilter, IActionFilter, IResultFilter
    {
        public const string KEY_START = "StartTime";
        public int THRESHOLD_MS_CRITICAL { get; init; }
        public int THRESHOLD_MS_ERROR { get; init; }
        public int THRESHOLD_MS_WARN { get; init; }

    /// <summary>
    /// Adds a performance-monitoring filter to all requests in the service.  This filter will measure the time taken by endpoints to better understand where we have room for improvement.
    /// </summary>
    /// <param name="warnMS">The allowed time threshold for an endpoint to complete normally.  If exceeded, a WARN log event is created.</param>
    /// <param name="errorMS">If this time limit is exceeded, the WARN event is escalated to an ERROR.</param>
    /// <param name="criticalMS">If this time limit is exceeded, the ERROR event is escalated to a CRITICAL log event.  It should be unreasonably high.</param>
    public PlatformPerformanceFilter(int warnMS = 500, int errorMS = 5_000, int criticalMS = 30_000)
    {
        THRESHOLD_MS_WARN = warnMS;
        THRESHOLD_MS_ERROR = errorMS;
        THRESHOLD_MS_CRITICAL = criticalMS;
        
        Log.Verbose(Owner.Default, $"{GetType().Name} threshold data initialized.", data: new
        {
            Thresholds = new
            {
                Warning = THRESHOLD_MS_WARN,
                Error = THRESHOLD_MS_ERROR,
                Critical = THRESHOLD_MS_CRITICAL
            }
        });
    }

    public void OnAuthorization(AuthorizationFilterContext context) => context.HttpContext.Items[KEY_START] = Diagnostics.Timestamp;

    /// <summary>
    /// This fires before any endpoint begins its work.  This is where we can mark a timestamp to measure our performance.
    /// </summary>
    public void OnActionExecuting(ActionExecutingContext context) { }

    /// <summary>
    /// This fires after an endpoint finishes its work, but before the result is sent back to the client.
    /// </summary>
    public void OnActionExecuted(ActionExecutedContext context)
    {
        string name = context.HttpContext.Request.Path.Value;
        long taken = TimeTaken(context);
        string message = $"{name} took a long time to execute.";

        object diagnostics = LogObject(context, "ActionExecuted", taken);

        if (taken > THRESHOLD_MS_CRITICAL && !context.ControllerHasAttribute<IgnorePerformance>())
            Log.Verbose(Owner.Default, message, data: diagnostics);
    }

    public void OnResultExecuting(ResultExecutingContext context) { }

    /// <summary>
    /// This fires after the result has been sent back to the client, indicating the total time taken.
    /// </summary>
    public void OnResultExecuted(ResultExecutedContext context)
    {
        string name = context.HttpContext.Request.Path.Value;
        long taken = TimeTaken(context);
        string message = $"{name} took a long time to respond to the client.";
        object diagnostics = LogObject(context, "ResultExecuted", taken);

        if (context.ControllerHasAttribute<IgnorePerformance>())
        {
            Log.Verbose(Owner.Default, $"Performance metrics ignored for {name}.");
            return;
        }

        // Log the time taken
        #if DEBUG
        if (PlatformEnvironment.IsLocal)
            Log.Verbose(Owner.Default, message: $"{name} took {taken}ms to execute", data: diagnostics);
        #else
        if (taken > THRESHOLD_MS_CRITICAL && THRESHOLD_MS_CRITICAL > 0)
            Log.Critical(Owner.Default, message, data: diagnostics);
        else if (taken > THRESHOLD_MS_ERROR && THRESHOLD_MS_ERROR > 0)
            Log.Error(Owner.Default, message, data: diagnostics);
        else if (taken > THRESHOLD_MS_WARN && THRESHOLD_MS_WARN > 0)
            Log.Warn(Owner.Default, message, data: diagnostics);
        #endif
    }

    /// <summary>
    /// Creates the data object for the log.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="step"></param>
    /// <param name="timeTaken"></param>
    /// <returns>An anonymous object for logging data.</returns>
    private object LogObject(ActionContext context, string step, long timeTaken) => new
    {
        RequestUrl = context.HttpContext.Request.Path.Value,
        StartTime = context.HttpContext.Items[KEY_START],
        Step = step,
        TimeAllowed = THRESHOLD_MS_WARN,
        TimeTaken = timeTaken
    };

    /// <summary>
    /// Calculates the amount of time taken by the endpoint.
    /// </summary>
    /// <param name="context">The ActionContext from the filter.</param>
    /// <returns>A long value indicating the time taken in milliseconds.  The value is negative if the calculation fails.</returns>
    private static long TimeTaken(ActionContext context)
    {
        try
        {
            // ReSharper disable once PossibleNullReferenceException
            return Diagnostics.TimeTaken((long)context.HttpContext.Items[KEY_START]);
        }
        catch (Exception e)
        {
            Log.Warn(Owner.Default, $"FilterContext was missing key: {KEY_START}.  Could not calculate time taken.", exception: e);
            return -1;
        }
    }
}