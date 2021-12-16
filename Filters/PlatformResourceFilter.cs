using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;
using RestSharp.Serialization.Json;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

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
				GenericData query = new GenericData();
				GenericData body = null;
				
				foreach (KeyValuePair<string, StringValues> pair in context.HttpContext.Request.Query)
					query[pair.Key] = pair.Value.ToString();
				if (context.HttpContext.Request.Method != "GET")
				{
					context.HttpContext.Request.BodyReader.TryRead(out ReadResult result);
					string json = Encoding.UTF8.GetString(result.Buffer.FirstSpan);
					context.HttpContext.Request.BodyReader.AdvanceTo(result.Buffer.End);
					context.HttpContext.Request.BodyReader.Complete();
					body = json;
				}

				body?.Combine(query); // If both the body and query have the same key, the values in the body have priority.
				body ??= query;
				
				context.HttpContext.Items[KEY_BODY] = body;
				// context.HttpContext.Items[KEY_BODY] = JsonDocument.Parse(json, JsonHelper.DocumentOptions);
			}
			catch (Exception e)
			{
				Log.Warn(Owner.Default, "The request body could not be read.", data: Converter.ContextToEndpointObject(context), exception: e);
			}
		}

		public void OnResourceExecuted(ResourceExecutedContext context) { }
	}
}