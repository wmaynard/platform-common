using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Dynamic;
using System.IO.Pipelines;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Filters;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Web
{
	public abstract class PlatformController : ControllerBase
	{
		protected const string AUTH = "Authorization";
		public static readonly string TokenAuthEndpoint = RumbleEnvironment.Variable("RUMBLE_TOKEN_VERIFICATION");

		protected readonly IConfiguration _config;
		protected PlatformController(IConfiguration config)
		{
			_config = config;
			TokenVerification = new WebRequest(TokenAuthEndpoint, Method.GET);
		}
		protected WebRequest TokenVerification { get; set; }

		public ObjectResult Problem(string detail) => Problem(value: new { DebugText = detail });

		public OkObjectResult Problem(object value)
		{
			return base.Ok(Merge(new { Success = false }, value));
		}

		public new OkObjectResult Ok() => base.Ok(null);
		public OkObjectResult Ok(string message) => Ok( new { Message = message });
		private new OkObjectResult Ok(object value)
		{
			return base.Ok(Merge(new { Success = true }, value));
		}

		public OkObjectResult Ok(params object[] objects)
		{
			return this.Ok(value: Merge(objects));
		}

		protected static object Merge(params object[] objects)
		{
			if (objects == null)
				return null;
			object output = new { };
			foreach (object o in objects)
				output = Merge(foo: output, bar: o);
			return output;
		}

		protected static object Merge(object foo, object bar)
		{
			if (foo == null || bar == null)
				return foo ?? bar ?? new ExpandoObject();

			ExpandoObject expando = new ExpandoObject();
			IDictionary<string, object> result = (IDictionary<string, object>)expando;
			
			if (foo is ExpandoObject oof)	// Special handling is required when trying to merge two ExpandoObjects together.
				MergeExpando(ref result, oof);
			else
				foreach (PropertyInfo fi in foo.GetType().GetProperties())
					result[JsonNamingPolicy.CamelCase.ConvertName(fi.Name)] = fi.GetValue(foo, null);
			
			if (bar is ExpandoObject rab)
				MergeExpando(ref result, rab);
			else
				foreach (PropertyInfo fi in bar.GetType().GetProperties())
					result[JsonNamingPolicy.CamelCase.ConvertName(fi.Name)] = fi.GetValue(bar, null);
			return result;
		}

		private static void MergeExpando(ref IDictionary<string, object> expando, ExpandoObject expando2)
		{
			IDictionary<string, object> dict = (IDictionary<string, object>) expando2;
			foreach (string key in dict.Keys)
				expando[JsonNamingPolicy.CamelCase.ConvertName(key)] = dict[key];
		}

		protected static JToken ExtractOptionalValue(string name, JObject body)
		{
			return ExtractValue(name, body, required: false);
		}
		protected static JToken ExtractRequiredValue(string name, JObject body)
		{
			return ExtractValue(name, body);
		}
		private static JToken ExtractValue(string name, JObject body, bool required = true)
		{
			JToken output = body[name];
			if (required && output == null)
				throw new FieldNotProvidedException(name);
			return output;
		}

		public static TokenInfo ValidateAdminToken(string token)
		{
			TokenInfo output = ValidateToken(token);
			if (!output.IsAdmin)
				throw new InvalidTokenException(token, TokenAuthEndpoint);
			return output;
		}
		
		/// <summary>
		/// Sends a GET request to a token service (currently player-service) to validate a token.
		/// </summary>
		/// <param name="token">The JWT as it appears in the Authorization header, including "Bearer ".</param>
		/// <returns>Information encoded in the token.</returns>
		public static TokenInfo ValidateToken(string token)
		{
			if (token == null)
				throw new InvalidTokenException(token, TokenAuthEndpoint);
			long timestamp = Diagnostics.Timestamp;
			JObject result = null;

			try
			{
				result = WebRequest.Get(TokenAuthEndpoint, token);
			}
			catch (Exception e)
			{
				throw new InvalidTokenException(token, TokenAuthEndpoint, e);
			}
			bool success = (bool)result["success"];
			if (!success)
				throw new InvalidTokenException(token, TokenAuthEndpoint, new Exception((string) result["error"]));
			try
			{
				TokenInfo output = new TokenInfo(token)
				{
					AccountId = ExtractRequiredValue(TokenInfo.FRIENDLY_KEY_ACCOUNT_ID, result).ToObject<string>(),
					Discriminator = ExtractOptionalValue(TokenInfo.FRIENDLY_KEY_DISCRIMINATOR, result)?.ToObject<int?>() ?? -1,
					Expiration = DateTime.UnixEpoch.AddSeconds(ExtractRequiredValue(TokenInfo.FRIENDLY_KEY_EXPIRATION, result).ToObject<long>()),
					Issuer = ExtractRequiredValue(TokenInfo.FRIENDLY_KEY_ISSUER, result).ToObject<string>(),
					ScreenName = ExtractOptionalValue(TokenInfo.FRIENDLY_KEY_SCREENNAME, result).ToObject<string>(),
					SecondsRemaining = ExtractRequiredValue(TokenInfo.FRIENDLY_KEY_SECONDS_REMAINING, result).ToObject<double>(),
					IsAdmin = ExtractOptionalValue(TokenInfo.FRIENDLY_KEY_IS_ADMIN, result)?.ToObject<bool>() ?? false 
				};
				Log.Verbose(Owner.Platform, $"Time taken to verify the token: {Diagnostics.TimeTaken(timestamp):N0}ms.");
				return output;
			}
			catch (Exception e)
			{
				throw new InvalidTokenException(token, TokenAuthEndpoint, e);
			}
		}
		
		[HttpGet, Route(template: "/health")]
		public abstract ActionResult HealthCheck();

		public static object CollectionResponseObject(IEnumerable<object> objects)
		{
			ExpandoObject expando = new ExpandoObject();
			IDictionary<string, object> output = (IDictionary<string, object>) expando;
			// Use the Type from the IEnumerable; otherwise if it's an empty enumerable it will throw an exception
			output[objects.GetType().GetGenericArguments()[0].Name + "s"] = objects;
			return output;
		}
		
		
		public string JTokenToRawJSON(JToken token)
		{
			return token.ToString(Formatting.None);
		}

		protected JObject Body
		{
			get
			{
				try
				{
					Request.BodyReader.TryRead(out ReadResult result);
					string json = Encoding.UTF8.GetString(result.Buffer.FirstSpan);
					Request.BodyReader.Complete();
					return JObject.Parse(json);
				}
				catch (InvalidOperationException)
				{
					Log.Error(Owner.Platform, "You may only read the request body once.  Store the value instead.");
					return null;
				}
				
			}
		}

		protected TokenInfo TokenInfo
		{
			get
			{
				try
				{
					return (TokenInfo)Request.HttpContext.Items[PlatformAuthorizationFilter.KEY_TOKEN];
				}
				catch (Exception)
				{
					Log.Warn(Owner.Platform, "TokenInfo was requested from the HttpContext but none was found.");
					return null;
				}
			}
		}
	}
}