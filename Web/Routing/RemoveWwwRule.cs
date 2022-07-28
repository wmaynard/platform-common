using System;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Net.Http.Headers;

namespace Rumble.Platform.Common.Web.Routing;

/// <summary>
/// This rule gets rid of the "www" in requests.  Simpler URLs are friendlier, and prefixing requests with "www"
/// is not needed.  The one exception to this is if you need to attach the project to a CDN to accomodate heavy traffic.
/// </summary>
public class RemoveWwwRule : PlatformRewriteRule
{
  private const string WWW = "www.";

  protected override RuleResult Apply(HttpRequest request, HttpResponse response)
  {
    HostString host = request.Host;

    if (!host.Host.StartsWith(WWW, StringComparison.OrdinalIgnoreCase))
      return default;
    
    string newPath = request.Scheme + "://" + host.Value.Replace(WWW, "") + request.PathBase + request.Path + request.QueryString;
    response.StatusCode = (int) HttpStatusCode.MovedPermanently;
    response.Headers[HeaderNames.Location] = newPath;
    
    return RuleResult.EndResponse;
  }
}