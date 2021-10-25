using System;
using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;

namespace Rumble.Platform.Common.Web
{
	[JsonObject]
	public class TokenInfo : PlatformDataModel
	{
		public const string DB_KEY_ACCOUNT_ID = "aid";
		public const string DB_KEY_AUTHORIZATION = "auth";
		public const string DB_KEY_DISCRIMINATOR = "d";
		public const string DB_KEY_EMAIL_ADDRESS = "@";
		public const string DB_KEY_EXPIRATION = "exp";
		public const string DB_KEY_ISSUER = "iss";
		public const string DB_KEY_SCREENNAME = "sn";
		public const string DB_KEY_IS_ADMIN = "su";
		public const string DB_KEY_IP_ADDRESS = "ip";
		
		public const string FRIENDLY_KEY_ACCOUNT_ID = "aid";
		public const string FRIENDLY_KEY_DISCRIMINATOR = "discriminator";
		public const string FRIENDLY_KEY_EMAIL_ADDRESS = "email";
		public const string FRIENDLY_KEY_EXPIRATION = "expiration";
		public const string FRIENDLY_KEY_IP_ADDRESS = "ip";
		public const string FRIENDLY_KEY_ISSUER = "issuer";
		public const string FRIENDLY_KEY_SCREENNAME = "screenname";
		public const string FRIENDLY_KEY_SECONDS_REMAINING = "secondsRemaining";
		public const string FRIENDLY_KEY_IS_ADMIN = "isAdmin";
		public const string FRIENDLY_KEY_USERNAME = "username";
		
		[BsonIgnore]
		[JsonIgnore]
		public string Authorization { get; private set; }
		[BsonElement(DB_KEY_ACCOUNT_ID)]
		[JsonProperty(PropertyName = FRIENDLY_KEY_ACCOUNT_ID, NullValueHandling = NullValueHandling.Ignore)]
		public string AccountId { get; set; }
		[BsonElement(DB_KEY_DISCRIMINATOR)]
		[JsonProperty(PropertyName = FRIENDLY_KEY_DISCRIMINATOR, DefaultValueHandling = DefaultValueHandling.Ignore)]
		public int Discriminator { get; set; }
		[BsonElement(DB_KEY_EMAIL_ADDRESS), BsonIgnoreIfNull]
		[JsonProperty(FRIENDLY_KEY_EMAIL_ADDRESS, NullValueHandling = NullValueHandling.Ignore)]
		public string Email { get; set; }
		[BsonElement(DB_KEY_EXPIRATION)]
		[JsonProperty(PropertyName = FRIENDLY_KEY_EXPIRATION, DefaultValueHandling = DefaultValueHandling.Ignore)]
		public long Expiration { get; set; }
		[BsonElement(DB_KEY_IP_ADDRESS), BsonIgnoreIfNull]
		[JsonProperty(PropertyName = FRIENDLY_KEY_IP_ADDRESS, NullValueHandling = NullValueHandling.Ignore)]
		public string IpAddress { get; set; }
		[BsonElement(DB_KEY_ISSUER)]
		[JsonProperty(PropertyName = FRIENDLY_KEY_ISSUER, NullValueHandling = NullValueHandling.Ignore)]
		public string Issuer { get; set; }
		[BsonElement(DB_KEY_IS_ADMIN)]
		[JsonProperty(PropertyName = FRIENDLY_KEY_IS_ADMIN, DefaultValueHandling = DefaultValueHandling.Ignore)]
		public bool IsAdmin { get; set; }
		[BsonElement(DB_KEY_SCREENNAME)]
		[JsonProperty(PropertyName = FRIENDLY_KEY_SCREENNAME, NullValueHandling = NullValueHandling.Ignore)]
		public string ScreenName { get; set; }
		[BsonIgnore]
		[JsonProperty(PropertyName = FRIENDLY_KEY_SECONDS_REMAINING, DefaultValueHandling = DefaultValueHandling.Ignore)]
		public double SecondsRemaining { get; set; }
		[BsonIgnore]
		[JsonProperty(PropertyName = FRIENDLY_KEY_USERNAME, NullValueHandling = NullValueHandling.Ignore)]
		public string Username => $"{ScreenName ?? "(Unknown Screenname)"}#{(Discriminator.ToString() ?? "").PadLeft(4, '?')}{(IsAdmin ? " (Administrator)" : "")}";

		[BsonIgnore, JsonIgnore] public bool IsExpired => Expiration <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		
		public TokenInfo(string auth = null)
		{
			Authorization = auth;
		}
	}
}