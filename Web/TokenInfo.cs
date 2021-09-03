using System;
using Newtonsoft.Json;

namespace Rumble.Platform.Common.Web
{
	[JsonObject]
	public class TokenInfo
	{
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public string AccountId { get; set; }
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
		public int Discriminator { get; set; }
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
		public DateTime Expiration { get; set; }
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public string Issuer { get; set; }
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
		public bool IsAdmin { get; set; }
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public string ScreenName { get; set; }
		[JsonProperty]
		public double SecondsRemaining { get; set; }
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public string Username => $"{ScreenName ?? "(Unknown Screenname)"}#{(Discriminator.ToString() ?? "").PadLeft(4, '?')}{(IsAdmin ? " (Administrator)" : "")}";

	}
}