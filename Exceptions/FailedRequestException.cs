using System;
using Newtonsoft.Json;
using RestSharp;

namespace Rumble.Platform.Common.Exceptions
{
	public class FailedRequestException : PlatformException
	{
		[JsonProperty(NullValueHandling = NullValueHandling.Include)]
		public string Url { get; init; }
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public new string Data { get; init; }
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public IRestResponse Response { get; init; }
		public FailedRequestException(string url, string json = null, IRestResponse response = null) : base("An HTTP request failed.")
		{
			Url = url;
			Data = json;
			Response = response;
		}
	}
}