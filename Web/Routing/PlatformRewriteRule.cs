using System;
using System.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Rewrite;
using RCL.Logging;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Web.Routing;

public abstract class PlatformRewriteRule : IRule
{
  protected static readonly string WEB_ROOT = Directory.GetCurrentDirectory() + "/wwwroot";
  
  public void ApplyRule(RewriteContext context)
  {
    RuleResult result = default;
    try
    {
      // Encapsulate every rule in a try/catch block so that a failure doesn't prevent other rule executions.
      result = Apply(context.HttpContext.Request, context.HttpContext.Response);
    }
    catch
    {
      Log.Local(Owner.Will, $"{context.HttpContext.Request.Path.Value} | Unable to process RewriteRule: {GetType().Name}.");
    }
    context.Result = result == default
      ? RuleResult.ContinueRules
      : result;
  }

  protected abstract RuleResult Apply(HttpRequest request, HttpResponse response);
}