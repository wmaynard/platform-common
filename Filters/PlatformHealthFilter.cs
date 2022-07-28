using System.Linq;
using Microsoft.AspNetCore.Mvc.Filters;
using RCL.Logging;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Filters;

public class PlatformHealthFilter : PlatformFilter, IActionFilter, IResultFilter
{
  public void OnActionExecuting(ActionExecutingContext context)
  {
    HealthMonitor monitor = context.GetControllerAttributes<HealthMonitor>().FirstOrDefault();

    if (monitor == null)
      return;
    context.GetService<HealthService>().Add(monitor.Weight);
  }

  public void OnActionExecuted(ActionExecutedContext context)
  {
    HealthMonitor monitor = context.GetControllerAttributes<HealthMonitor>().FirstOrDefault();

    if (monitor == null)
      return;
    
    // Ignore RequiredFieldMissing issues.  This happens when GenericData, for example, fails a Require().
    if (context.Exception is not PlatformException ex)
      return;
    
    switch (ex.Code)
    {
      case ErrorCode.RequiredFieldMissing: // This is a poorly formed request and isn't indicative of service failure
        Log.Warn(Owner.Default, "A monitored endpoint was expecting a required field but did not receive it.", exception: ex);
        context.GetService<HealthService>().Score(monitor.Weight);
        break;
      default:
        break;
    }
  }
  
  public void OnResultExecuting(ResultExecutingContext context) { }

  // This only occurs after we know we haven't had any exceptions / have returned early.
  public void OnResultExecuted(ResultExecutedContext context)
  {
    HealthMonitor monitor = context.GetControllerAttributes<HealthMonitor>().FirstOrDefault();

    if (monitor == null)
      return;

    HealthService service = context.GetService<HealthService>();
    service.Score(monitor.Weight);
  }
}