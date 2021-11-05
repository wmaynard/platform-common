using System;
using System.Collections.Generic;
using System.Text.Json;
using RestSharp;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Web;

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

		public JsonDocument Send(Dictionary<string, string> queryParameters)
		{
			return Send(jsonBody: null, queryParameters: queryParameters);
		}
		/// <summary>
		/// Sends a request.
		/// </summary>
		/// <param name="jsonBody">A string representation of a JSON payload.</param>
		/// <param name="queryParameters"></param>
		/// <returns></returns>
		public JsonDocument Send(string jsonBody = null, Dictionary<string, string> queryParameters = null)
		{
			RestRequest request = new RestRequest(Method);
			if (Authorization != null)
				request.AddHeader("Authorization", Authorization);
			if (jsonBody != null)
			{
				request.AddJsonBody(jsonBody);
				request.RequestFormat = DataFormat.Json;
				request.AddHeader("Content-Type", "application/json");
			}

			if (queryParameters != null)
				foreach (KeyValuePair<string, string> kvp in queryParameters)
					request.AddQueryParameter(kvp.Key, kvp.Value);
			IRestResponse response = Client.Execute(request);
			// IRestResponse<Dictionary<string, object>> responseData = Client.Execute<Dictionary<string, object>>(request);
			if (!response.IsSuccessful)
				throw new FailedRequestException(Endpoint.OriginalString, responseData: new
				{
					HttpCode = response.StatusCode,
					Content = response.Content,
					Error = response.ErrorMessage,
					Exception = response.ErrorException,
					Description = response.StatusDescription
				});
			return JsonDocument.Parse(response.Content);
		}
		/// <summary>
		/// Sends a request.
		/// </summary>
		/// <param name="serializeMe">An object to be serialized to JSON.  By default, serializes JSON to lowerCamelCase.</param>
		/// <param name="queryParameters"></param>
		/// <returns></returns>
		public JsonDocument Send(object serializeMe, Dictionary<string, string> queryParameters = null)
		{
			// If serializeMe is null, This will send a body of "null", which doesn't make sense.
			// It's possible someone would send a null object to this function explicitly, so we should control for that.
			if (serializeMe == null)
				return Send(jsonBody: null, queryParameters: queryParameters);


			string json = JsonSerializer.Serialize(serializeMe, JsonHelper.SerializerOptions);
			return Send(json, queryParameters);
		}

		/// <summary>
		/// Sends a GET request to an endpoint.
		/// </summary>
		/// <param name="endpoint">The full URL.</param>
		/// <param name="auth"></param>
		/// <param name="queryParameters"></param>
		/// <returns>The Authorization header.  If using a token, be sure to include "Bearer " in front of it.</returns>
		public static JsonDocument Get(string endpoint, string auth = null, Dictionary<string, string> queryParameters = null)
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
		public static JsonDocument Post(string endpoint, string json, string auth = null, Dictionary<string, string> queryParameters = null)
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
		public static JsonDocument Post(string endpoint, object serializeMe, string auth = null, Dictionary<string, string> queryParameters = null)
		{
			return new WebRequest(endpoint, RestSharp.Method.POST, auth).Send(serializeMe, queryParameters);
		}

		public override string ToString() => $"{Enum.GetName(Method)} {Endpoint}";
	}
}