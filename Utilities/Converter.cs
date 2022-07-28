using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Rumble.Platform.Common.Utilities;

[Obsolete("Methods from this helper class should be moved to extension methods.")]
public static class Converter
{
  // public static object ContextToEndpointObject(FilterContext context) => new { Endpoint = context?.HttpContext.Request.Path.Value };
  // public static string ContextToEndpoint(FilterContext context) => context?.HttpContext.Request.Path.Value;
  public static string ContextToEndpoint(HttpContext context) => context?.Request.Path.Value;
}