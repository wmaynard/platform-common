using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Rumble.Platform.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Extensions;

public static class FilterContextExtension
{
	public static T[] GetControllerAttributes<T>(this FilterContext context) where T : Attribute =>
		context.ActionDescriptor is ControllerActionDescriptor descriptor
			? descriptor
				.ControllerTypeInfo
				.GetCustomAttributes(inherit: true)
				.Concat(descriptor.MethodInfo.GetCustomAttributes(inherit: true))
				.OfType<T>()
				.ToArray()
			: null;

	public static bool ControllerHasAttribute<T>(this FilterContext context) where T : Attribute => 
		context.GetControllerAttributes<T>()?.Any() 
		?? false;
	
	public static string GetEndpoint(this FilterContext context) => context?.HttpContext.Request.Path.Value;
	public static object GetEndpointAsObject(this FilterContext context) => new { Endpoint = context.GetEndpoint() };

	public static T GetService<T>(this FilterContext context) where T : PlatformService => context.HttpContext.RequestServices.GetService<T>();
}