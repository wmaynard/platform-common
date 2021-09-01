using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using RestSharp;
using Rumble.Platform.Common.Exceptions;

namespace Rumble.Platform.Common.Utilities
{
	public class WebRequest
	{
		private Uri Endpoint { get; set; }
		private RestClient Client { get; set; }
		private Method Method { get; set; }
		private string Authorization { get; set; }
		
		/// <summary>
		/// Creates a new web request.
		/// </summary>
		/// <param name="endpoint">The full URL to request.</param>
		/// <param name="method">GET / POST / PUT / etc</param>
		/// <param name="auth">The Authorization header.  If using a token, be sure to include "Bearer " in front of it.</param>
		public WebRequest(string endpoint, Method method, string auth = null)
		{
			Endpoint = new Uri(endpoint);
			Client = new RestClient(Endpoint);
			Method = method;
			Authorization = auth;
		}

		public Dictionary<string, object> Send(Dictionary<string, string> queryParameters)
		{
			return Send(jsonBody: null, queryParameters: queryParameters);
		}
		/// <summary>
		/// Sends a request.
		/// </summary>
		/// <param name="jsonBody">A string representation of a JSON payload.</param>
		/// <param name="queryParameters"></param>
		/// <returns></returns>
		public Dictionary<string, object> Send(string jsonBody = null, Dictionary<string, string> queryParameters = null)
		{
			RestRequest request = new RestRequest(Method);
			if (Authorization != null)
				request.AddHeader("Authorization", Authorization);
			if (jsonBody != null)
				request.AddJsonBody(jsonBody);
			if (queryParameters != null)
				foreach (KeyValuePair<string, string> kvp in queryParameters)
					request.AddQueryParameter(kvp.Key, kvp.Value);
			IRestResponse<Dictionary<string, object>> response = Client.Execute<Dictionary<string, object>>(request);
			if (!response.IsSuccessful)
				throw new RumbleException(response.ErrorMessage);
			return response.Data;
		}
		/// <summary>
		/// Sends a request.
		/// </summary>
		/// <param name="serializeMe">An object to be serialized to JSON.  By default, serializes JSON to lowerCamelCase.</param>
		/// <param name="queryParameters"></param>
		/// <param name="serializeLowerCamelCase">Set to false to allow object properties to remain UpperCamelCase when serialized as JSON.</param>
		/// <returns></returns>
		public Dictionary<string, object> Send(object serializeMe, Dictionary<string, string> queryParameters = null, bool serializeLowerCamelCase = true)
		{
			// If serializeMe is null, This will send a body of "null", which doesn't make sense.
			// It's possible someone would send a null object to this function explicitly, so we should control for that.
			if (serializeMe == null)
				return Send(jsonBody: null, queryParameters: queryParameters);
			
			string json = serializeLowerCamelCase
				? JsonConvert.SerializeObject(serializeMe, new JsonSerializerSettings() { ContractResolver = new CamelCasePropertyNamesContractResolver() })
				: JsonConvert.SerializeObject(serializeMe);
			return Send(json, queryParameters);
		}

		/// <summary>
		/// Sends a GET request to an endpoint.
		/// </summary>
		/// <param name="endpoint">The full URL.</param>
		/// <param name="auth"></param>
		/// <param name="queryParameters"></param>
		/// <returns>The Authorization header.  If using a token, be sure to include "Bearer " in front of it.</returns>
		public static Dictionary<string, object> Get(string endpoint, string auth = null, Dictionary<string, string> queryParameters = null)
		{
			return new WebRequest(endpoint, Method.GET, auth).Send(queryParameters);
		}
		/// <summary>
		/// Sends a POST request to an endpoint.
		/// </summary>
		/// <param name="endpoint">The full URL.</param>
		/// <param name="json">A json body for the request.</param>
		/// <param name="auth">The Authorization header.  If using a token, be sure to include "Bearer " in front of it.</param>
		/// <param name="queryParameters"></param>
		/// <returns></returns>
		public static Dictionary<string, object> Post(string endpoint, string json, string auth = null, Dictionary<string, string> queryParameters = null)
		{
			return new WebRequest(endpoint, RestSharp.Method.POST, auth).Send(json, queryParameters);
		}
		/// <summary>
		/// Sends a POST request to an endpoint.
		/// </summary>
		/// <param name="endpoint">The full URL.</param>
		/// <param name="serializeMe">An object to be serialized into JSON.</param>
		/// <param name="auth">The Authorization header.  If using a token, be sure to include "Bearer " in front of it.</param>
		/// <param name="queryParameters"></param>
		/// <param name="lowerCamelCase">Set to false to allow object properties to remain UpperCamelCase when serialized as JSON.</param>
		/// <returns></returns>
		public static Dictionary<string, object> Post(string endpoint, object serializeMe, string auth = null, Dictionary<string, string> queryParameters = null, bool lowerCamelCase = true)
		{
			return new WebRequest(endpoint, RestSharp.Method.POST, auth).Send(serializeMe, queryParameters, lowerCamelCase);
		}

		public override string ToString() => $"{Enum.GetName(Method)} {Endpoint}";
	}
}