using System;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace Rumble.Platform.Common.Web
{
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
		[JsonPropertyName(FRIENDLY_KEY_ACCOUNT_ID), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string AccountId { get; set; }
		[BsonElement(DB_KEY_DISCRIMINATOR)]
		[JsonPropertyName(FRIENDLY_KEY_DISCRIMINATOR), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public int Discriminator { get; set; }
		[BsonElement(DB_KEY_EMAIL_ADDRESS), BsonIgnoreIfNull]
		[JsonPropertyName(FRIENDLY_KEY_EMAIL_ADDRESS), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string Email { get; set; }
		[BsonElement(DB_KEY_EXPIRATION)]
		[JsonPropertyName(FRIENDLY_KEY_EXPIRATION), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public long Expiration { get; set; }
		[BsonElement(DB_KEY_IP_ADDRESS), BsonIgnoreIfNull]
		[JsonPropertyName(FRIENDLY_KEY_IP_ADDRESS), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string IpAddress { get; set; }
		[BsonElement(DB_KEY_ISSUER)]
		[JsonPropertyName(FRIENDLY_KEY_ISSUER), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string Issuer { get; set; }
		[BsonElement(DB_KEY_IS_ADMIN)]
		[JsonPropertyName(FRIENDLY_KEY_IS_ADMIN), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public bool IsAdmin { get; set; }
		[BsonElement(DB_KEY_SCREENNAME)]
		[JsonPropertyName(FRIENDLY_KEY_SCREENNAME), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string ScreenName { get; set; }
		[BsonIgnore]
		[JsonPropertyName(FRIENDLY_KEY_SECONDS_REMAINING), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public double SecondsRemaining { get; set; }
		[BsonIgnore]
		[JsonPropertyName(FRIENDLY_KEY_USERNAME), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string Username => $"{ScreenName ?? "(Unknown Screenname)"}#{(Discriminator.ToString() ?? "").PadLeft(4, '?')}{(IsAdmin ? " (Administrator)" : "")}";

		[BsonIgnore, JsonIgnore] public bool IsExpired => Expiration <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		
		public TokenInfo(string auth = null)
		{
			Authorization = auth;
		}
	}
}