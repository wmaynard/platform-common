using System;
using System.IO.Pipelines;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json.Linq;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Filters
{
	public class PlatformBodyReaderFilter : IResourceFilter
	{
		public const string KEY_BODY = "RequestBody";

		public void OnResourceExecuting(ResourceExecutingContext context)
		{
			try
			{
				if (context.HttpContext.Request.Method == "GET")
					return;
				context.HttpContext.Request.BodyReader.TryRead(out ReadResult result);
				string json = Encoding.UTF8.GetString(result.Buffer.FirstSpan);
				context.HttpContext.Request.BodyReader.AdvanceTo(result.Buffer.End);
				context.HttpContext.Request.BodyReader.Complete();
				context.HttpContext.Items[KEY_BODY] = JObject.Parse(json);
			}
			catch (Exception e)
			{
				Log.Warn(Owner.Default, "The request body could not be read.", data: Converter.ContextToEndpointObject(context), exception: e);
			}
		}

		public void OnResourceExecuted(ResourceExecutedContext context) { }
	}
}