using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Rumble.Platform.Common.Utilities
{
	public static class Converter
	{
		public static object ContextToEndpointObject(FilterContext context) => new { Endpoint = context.HttpContext.Request.Path.Value };
		public static string ContextToEndpoint(FilterContext context) => context.HttpContext.Request.Path.Value;
	}
}