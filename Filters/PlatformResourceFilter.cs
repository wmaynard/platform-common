using System;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using RestSharp.Serialization.Json;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Filters
{
	public class PlatformResourceFilter : PlatformBaseFilter, IResourceFilter
	{
		public const string KEY_AUTHORIZATION = "EncryptedToken";
		public const string KEY_BODY = "RequestBody";

		public void OnResourceExecuting(ResourceExecutingContext context)
		{
			try
			{
				string auth = context.HttpContext.Request.Headers
					.First(kvp => kvp.Key == "Authorization")
					.Value
					.First()
					.Replace("Bearer ", "");
					context.HttpContext.Items[KEY_AUTHORIZATION] = auth;
			}
			catch (Exception)
			{
				Log.Verbose(Owner.Default, "The request authorization could not be read.");
			}
			try
			{
				if (context.HttpContext.Request.Method == "GET")
					return;
				context.HttpContext.Request.BodyReader.TryRead(out ReadResult result);
				string json = Encoding.UTF8.GetString(result.Buffer.FirstSpan);
				context.HttpContext.Request.BodyReader.AdvanceTo(result.Buffer.End);
				context.HttpContext.Request.BodyReader.Complete();
				context.HttpContext.Items[KEY_BODY] = JsonDocument.Parse(json);
			}
			catch (Exception e)
			{
				Log.Warn(Owner.Default, "The request body could not be read.", data: Converter.ContextToEndpointObject(context), exception: e);
			}
		}

		public void OnResourceExecuted(ResourceExecutedContext context) { }
	}
}