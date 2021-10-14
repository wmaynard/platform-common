using Microsoft.AspNetCore.Mvc.Filters;

namespace Rumble.Platform.Common.Filters
{
	public abstract class PlatformBaseFilter : IActionFilter
	{
		public virtual void OnActionExecuting(ActionExecutingContext context)
		{
			
		}

		public virtual void OnActionExecuted(ActionExecutedContext context)
		{
			
		}

		protected static object EndpointObject(FilterContext context) => new { Endpoint = context.HttpContext.Request.Path.Value };
	}
}