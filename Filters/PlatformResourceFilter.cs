using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;
using MongoDB.Driver;
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
			// Remove "Bearer " from the token.
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
			// Read the query parameters and request body and place them into a GenericData for later use in the endpoint.
			try
			{
				GenericData query = new GenericData();
				GenericData body = null;
				
				foreach (KeyValuePair<string, StringValues> pair in context.HttpContext.Request.Query)
					query[pair.Key] = pair.Value.ToString();
				if (context.HttpContext.Request.Method != "GET")
				{
					string json = "";
					
					if (!context.HttpContext.Request.BodyReader.TryRead(out ReadResult result))
						throw new Exception("reader.TryRead() failed when parsing the request body.");
					
					SequenceReader<byte> rdr = new SequenceReader<byte>(result.Buffer);
					while (!rdr.End)
					{
						json += Encoding.UTF8.GetString(rdr.CurrentSpan);
						rdr.Advance(rdr.CurrentSpan.Length);
					}
					
					body = json;
					
					context.HttpContext.Request.BodyReader.AdvanceTo(result.Buffer.End);
					context.HttpContext.Request.BodyReader.Complete();
				}

				body?.Combine(query); // If both the body and query have the same key, the values in the body have priority.
				body ??= query;
				
				context.HttpContext.Items[KEY_BODY] = body;
			}
			catch (Exception e)
			{
				Log.Warn(Owner.Default, "The request body or query parameters could not be read.", data: Converter.ContextToEndpointObject(context), exception: e);
			}
		}

		public void OnResourceExecuted(ResourceExecutedContext context) { }
	}
}