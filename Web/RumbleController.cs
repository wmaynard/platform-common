using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Dynamic;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace Rumble.Platform.Common.Web
{
	public abstract class RumbleController : ControllerBase
	{
		protected const string AUTH = "Authorization";
		protected abstract string TokenAuthEndpoint { get; }
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
		public override OkObjectResult Ok(object value)
		{
			return base.Ok(Merge(new { Success = true }, value));
		}

		protected static object Merge(params object[] objects)
		{
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
		
		/// <summary>
		/// Sends a GET request to a token service (currently player-service) to validate a token.
		/// </summary>
		/// <param name="verificationEndpoint">e.g. http://localhost:8081/player/verify</param>
		/// <param name="token">The JWT as it appears in the Authorization header, including "Bearer ".</param>
		/// <returns>Information encoded in the token.</returns>
		protected TokenInfo ValidateToken(string token)
		{
			if (token == null)
				throw new InvalidTokenException();
			Dictionary<string, object> result = InternalApiCall(TokenAuthEndpoint, token);
			TokenInfo output = new TokenInfo()
			{
				AccountId = (string)result["aid"],
				Expiration = DateTime.UnixEpoch.AddSeconds((long)result["expiration"]),
				Issuer = (string)result["issuer"]
			};
			if (output.Expiration.Subtract(DateTime.Now).TotalMilliseconds <= 0)
				throw new InvalidTokenException();
			return output;
		}
		/// <summary>
		/// Call on other Rumble web services.  Currently only supports GET requests.
		/// </summary>
		/// <param name="endpoint">The full URL to request.</param>
		/// <param name="authorization">The token to pass along.</param>
		/// <returns>A Dictionary<string, object> of the JSON response.</returns>  TODO: Return JSON
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
	}
}
//dotnet pack --configuration Release
//dotnet nuget push "bin/Release/OctocatApp.1.0.0.nupkg"  --api-key YOUR_GITHUB_PAT --source "github"