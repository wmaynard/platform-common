using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using RestSharp;
using Rumble.Platform.Common.Exceptions;

namespace Rumble.Platform.Common.Web
{
	public abstract class RumbleController : ControllerBase
	{
		protected const string AUTH = "Authorization";
		protected abstract string TokenAuthEndpoint { get; }

		protected readonly IConfiguration _config;
		protected RumbleController(IConfiguration config) => _config = config;
		internal void Throw(string message, Exception exception = null)
		{
			throw new Exception(message, innerException: exception);
		}

		public ObjectResult Problem(string detail) => Problem(value: new { DebugText = detail });

		public OkObjectResult Problem(object value)
		{
			return base.Ok(Merge(new { Success = false }, value));
		}

		public new OkObjectResult Ok() => Ok(null);
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

		protected TokenInfo ValidateAdminToken(string token)
		{
			TokenInfo output = ValidateToken(token);
			if (!output.IsAdmin)
				throw new InvalidTokenException("Unauthorized");
			return output;
		}
		
		/// <summary>
		/// Sends a GET request to a token service (currently player-service) to validate a token.
		/// </summary>
		/// <param name="token">The JWT as it appears in the Authorization header, including "Bearer ".</param>
		/// <returns>Information encoded in the token.</returns>
		protected TokenInfo ValidateToken(string token)
		{
			if (token == null)
				throw new InvalidTokenException();
			Dictionary<string, object> result = null;

			try
			{
				result = InternalApiCall(TokenAuthEndpoint, token);
			}
			catch (Exception e)
			{
				throw new InvalidTokenException($"Exception encountered in request to {TokenAuthEndpoint}", e);
			}
			bool success = (bool)result["success"];
			if (!success)
				throw new InvalidTokenException((string) result["error"]);
			try
			{
				TokenInfo output = new TokenInfo()
				{
					AccountId = (string) result["aid"],
					Discriminator = Convert.ToInt32(result["discriminator"]),
					Expiration = DateTime.UnixEpoch.AddSeconds((long) result["expiration"]),
					Issuer = (string) result["issuer"],
					ScreenName = (string) result["screenName"],
					SecondsRemaining = (double) result["secondsRemaining"]
				};
				output.IsAdmin = result.ContainsKey("isAdmin") && (bool) result["isAdmin"];
				return output;
			}
			catch (Exception e)
			{
				throw new InvalidTokenException($"Could not verify token.", e);
			}
		}
		/// <summary>
		/// Call on other Rumble web services.  Currently only supports GET requests.
		/// </summary>
		/// <param name="endpoint">The full URL to request.</param>
		/// <param name="authorization">The token to pass along.</param>
		/// <returns>A Dictionary<string, object> of the JSON response.</returns>
		private static Dictionary<string, object> InternalApiCall(string endpoint, string authorization = null)
		{
			Uri baseUrl = new Uri(endpoint);
			IRestClient client = new RestClient(baseUrl);
			IRestRequest request = new RestRequest("get", Method.GET);
			
			if (authorization != null)
				request.AddHeader("Authorization", authorization);

			IRestResponse<Dictionary<string, object>> response = client.Execute<Dictionary<string, object>>(request);
			if (!response.IsSuccessful)
				throw new Exception(response.ErrorMessage);
			
			return response.Data;
		}
		
		[HttpGet, Route(template: "/health")]
		public abstract ActionResult HealthCheck();

		public static object CollectionResponseObject(IEnumerable<object> objects)
		{
			ExpandoObject expando = new ExpandoObject();
			IDictionary<string, object> output = (IDictionary<string, object>) expando;
			output[objects.First().GetType().Name + "s"] = objects;
			return output;
		}
	}
}
// dotnet pack --configuration Release
// dotnet nuget push bin/Release/platform-csharp-common.1.x.x.nupkg --api-key YOUR_GITHUB_PAT --source github