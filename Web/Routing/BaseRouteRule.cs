using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Rewrite;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Web.Routing;

public class BaseRouteRule : PlatformRewriteRule
{
	private string Route { get; init; }

	public BaseRouteRule(string route) => Route = $"/{route}";

	protected override RuleResult Apply(HttpRequest request, HttpResponse response)
	{
		// Route is unspecified; keep processing rules.
		if (string.IsNullOrWhiteSpace(Route) || !request.Path.Value.StartsWith(Route) || Route == "/")
			return RuleResult.ContinueRules;

		// The base route set in Startup.cs does not actually correlate to Controller or static file routing.
		// By taking the substring after, we can have our service listen on two separate paths:
		//
		// foo-service with BaseRoute("foo")
		// /foo/admin/doSomething
		// /admin/doSomething
		// 
		// When deployed, only the first path will actually perform any work, since the k8s routing does not allow the
		// second one.  However, the second one is nice when working locally, rather than having to always add the base
		// route in every request.
		request.Path = new PathString(request.Path.Value[Route.Length..]);

		return RuleResult.ContinueRules;
	}
}