using System;
using System.IO.Pipelines;
using System.Text;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json.Linq;

namespace Rumble.Platform.Common.Filters
{
	public class PlatformBodyReaderFilter : IActionFilter
	{
		public const string KEY_BODY = "RequestBody";
		public void OnActionExecuting(ActionExecutingContext context)
		{
			try
			{
				context.HttpContext.Request.BodyReader.TryRead(out ReadResult result);
				string json = Encoding.UTF8.GetString(result.Buffer.FirstSpan);
				context.HttpContext.Request.BodyReader.AdvanceTo(result.Buffer.End);
				context.HttpContext.Request.BodyReader.Complete();
				context.HttpContext.Items[KEY_BODY] = JObject.Parse(json);
			}
			catch (Exception e)
			{
				var foo = "bar";
			}
		}

		public void OnActionExecuted(ActionExecutedContext context)
		{
			
		}
	}
}