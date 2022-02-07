using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;
using Rumble.Platform.Common.Interop;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Filters
{
	public class PlatformResourceFilter : PlatformBaseFilter, IResourceFilter
	{
		public const string KEY_AUTHORIZATION = "EncryptedToken";
		public const string KEY_BODY = "RequestBody";
		public const string KEY_IP_ADDRESS = "IpAddress";
		public const string KEY_GEO_IP_DATA = "GeoIpData";
		private static readonly string[] NO_BODY = { "HEAD", "GET", "DELETE" }; // These HTTP methods ignore the body reader code.

		public void OnResourceExecuting(ResourceExecutingContext context)
		{
			PrepareIpAddress(context);
			PrepareToken(context);
			ReadBody(context);
		}

		public void OnResourceExecuted(ResourceExecutedContext context) { }

		// Read the query parameters and request body and place them into a GenericData for later use in the endpoint.
		private static void ReadBody(ActionContext context)
		{
			string json = "";
			try
			{
				GenericData query = new GenericData();
				GenericData body = null;
				
				foreach (KeyValuePair<string, StringValues> pair in context.HttpContext.Request.Query)
					query[pair.Key] = pair.Value.ToString();
				
				if (!NO_BODY.Contains(context.HttpContext.Request.Method))
				{
					if (context.HttpContext.Request.HasFormContentType)	// Request is using form-data or x-www-form-urlencoded
					{
						Log.Warn(Owner.Default, "Incoming request is using form-data, which converts all fields to a string.  JSON is strongly preferred.");
						body = new GenericData();
						foreach (string key in context.HttpContext.Request.Form.Keys)
							body[key] = context.HttpContext.Request.Form[key].ToString();
					}
					else												// Request is using JSON (preferred).
					{
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
						
						SlackDiagnostics.Log(context.HttpContext.Request.Path + " success", "Temporary slack spam, will be removed with next platform-common update.")
							.Tag(Owner.Default)
							.Attach("InputBody.txt", json)
							.Send()
							.Wait();
					}
				}

				body?.Combine(query); // If both the body and query have the same key, the values in the body have priority.
				body ??= query;
				
				context.HttpContext.Items[KEY_BODY] = body;
			}
			catch (Exception e)
			{
				string message = "The request body or query parameters could not be read.";
				Log.Warn(Owner.Default, message, data: new
				{
					InputBody = json,
					InputQuery = context.HttpContext.Request.Query
				}, exception: e);
				SlackDiagnostics.Log(message, e.Message)
					.Tag(Owner.Default)
					.Attach("InputBody.txt", json)
					.Send()
					.Wait();
			}
		}

		// Remove "Bearer " from the token.
		private static void PrepareToken(ActionContext context)
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
		}

		private static void PrepareIpAddress(ActionContext context)
		{
			try
			{
				string GetHeader(string key)
				{
					string output = context.HttpContext.Request.Headers[key].ToString();
					return string.IsNullOrWhiteSpace(output)
						? null
						: output;
				}
				
				// Note: in the Groovy services, all of these keys were capitalized.
				string ip = GetHeader("X-Real-IP")
					?? GetHeader("X-Forwarded-For")
					?? GetHeader("X-Original-Forwarded-For")
					?? GetHeader("Proxy-Client-IP")
					?? GetHeader("Client-IP")
					?? context.HttpContext.Connection.RemoteIpAddress?.ToString();
				
#if DEBUG
				// Working locally when you need geoIP data is tough, since the loopback address never yields anything.
				// This allows us to mock our location through environment.json.
				if (ip == "::1" && PlatformEnvironment.Variable("LOCAL_IP_OVERRIDE", out string value))
					ip = value;
#endif
				
				context.HttpContext.Items[KEY_IP_ADDRESS] = ip?.Replace("::ffff:", "");
			}
			catch (Exception e)
			{
				Log.Warn(Owner.Default, "The client's IP Address could not be read.", exception: e);
			}
			
		}
	}
}