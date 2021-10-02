using Newtonsoft.Json;

namespace Rumble.Platform.Common.Exceptions
{
	public class FailedRequestException : RumbleException
	{
		[JsonProperty(NullValueHandling = NullValueHandling.Include)]
		public string Url { get; init; }
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public new string Data { get; init; }
		public FailedRequestException(string url, string json = null) : base("An HTTP request failed.")
		{
			Url = url;
			Data = json;
		}
	}
}