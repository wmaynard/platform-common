using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;
using RCL.Logging;
using Rumble.Platform.Common.Interop;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Filters;

public class PlatformResourceFilter : PlatformBaseFilter, IResourceFilter
{
	public const string KEY_AUTHORIZATION = "EncryptedToken";
	public const string KEY_BODY = "RequestBody";
	public const string KEY_REQUEST_ORIGIN = "RequestOrigin"; // Used to identify which service made the request in situations where one service relies on another
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
					using Stream stream = context.HttpContext.Request.BodyReader.AsStream();
					using StreamReader reader = new StreamReader(stream);
					
					body = json = reader.ReadToEnd();
				}
			}

			body?.Combine(query); // If both the body and query have the same key, the values in the body have priority.
			body ??= query;

			context.HttpContext.Items[KEY_BODY] = body;
			context.HttpContext.Items[KEY_REQUEST_ORIGIN] = body?.Optional<string>("origin");
		}
		catch (Exception e)
		{
			Log.Error(Owner.Default, "The request body or query parameters could not be parsed into GenericData, probably as a result of incomplete or invalid JSON.", data: new
			{
				Details = "This can be the result of a request body exceeding its allowed buffer size.  Check nginx.ingress.kubernetes.io/client-body-buffer-size and consider increasing it."
			}, exception: e);
			SlackDiagnostics.Log("Request body failed to read.", "Unable to deserialize GenericData")
				.Attach($"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}.txt", string.IsNullOrEmpty(json) ? "(no json)" : json)
				.DirectMessage(Owner.Will)
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
			if (ip == "::1")
				ip = PlatformEnvironment.Optional("LOCAL_IP_OVERRIDE") ?? ip;
#endif
			
			context.HttpContext.Items[KEY_IP_ADDRESS] = ip?.Replace("::ffff:", "");
		}
		catch (Exception e)
		{
			Log.Warn(Owner.Default, "The client's IP Address could not be read.", exception: e);
		}
		
	}
}