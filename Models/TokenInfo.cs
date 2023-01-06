using System;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Models;

[BsonIgnoreExtraElements]
public class TokenInfo : PlatformDataModel
{
    internal const int CACHE_EXPIRATION = 600_000; // 10 minutes
    public const string KEY_TOKEN_OUTPUT = "token";
    public const string KEY_TOKEN_LEGACY_OUTPUT = "tokenInfo";
        
    public const string DB_KEY_ACCOUNT_ID = "aid";
    public const string DB_KEY_AUDIENCE = "aud";
    public const string DB_KEY_AUTHORIZATION = "auth";
    public const string DB_KEY_COUNTRY_CODE = "cc";
    public const string DB_KEY_DISCRIMINATOR = "d";
    public const string DB_KEY_EMAIL_ADDRESS = "@";
    public const string DB_KEY_EXPIRATION = "exp";
    public const string DB_KEY_IS_ADMIN = "su";
    public const string DB_KEY_ISSUED_AT = "iat";
    public const string DB_KEY_ISSUER = "iss";
    public const string DB_KEY_REQUESTER = "req";
    public const string DB_KEY_SCREENNAME = "sn";
    public const string DB_KEY_IP_ADDRESS = "ip";
    public const string DB_KEY_GAME = "gkey";

    public const string FRIENDLY_KEY_ACCOUNT_ID = "accountId";
    public const string FRIENDLY_KEY_AUDIENCE = "audience";
    public const string FRIENDLY_KEY_COUNTRY_CODE = "country";
    public const string FRIENDLY_KEY_DISCRIMINATOR = "discriminator";
    public const string FRIENDLY_KEY_EMAIL_ADDRESS = "email";
    public const string FRIENDLY_KEY_EXPIRATION = "expiration";
    public const string FRIENDLY_KEY_IP_ADDRESS = "ip";
    public const string FRIENDLY_KEY_IS_ADMIN = "isAdmin";
    public const string FRIENDLY_KEY_ISSUED_AT = "issuedAt";
    public const string FRIENDLY_KEY_ISSUER = "issuer";
    public const string FRIENDLY_KEY_REQUESTER = "origin";
    public const string FRIENDLY_KEY_SCREENNAME = "screenname";
    public const string FRIENDLY_KEY_SECONDS_REMAINING = "secondsRemaining";
    public const string FRIENDLY_KEY_USERNAME = "username";
    public const string FRIENDLY_KEY_GAME = "game";
    
    // TODO: RequestComponent?  Something to track who requested the token?
    // TODO: HasAccessTo(component) method, based on "aud"

    [BsonIgnore]
    [JsonIgnore]
    public string Authorization { get; set; }

    /// <summary>
    /// Alias for AccountId, comes back in JSON responses as "aid", which is a relic from Groovy days.  The new standard is
    /// "accountId" for readability and clarity.
    /// </summary>
    [Obsolete("Use AccountId instead.  AccountIdLegacy is only for API consumers / JSON serialization.")]
    [BsonIgnore]
    [JsonInclude, JsonPropertyName("aid"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string AccountIdLegacy => AccountId;

    [BsonElement(DB_KEY_ACCOUNT_ID)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_ACCOUNT_ID), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string AccountId { get; set; }
    
    [BsonElement(DB_KEY_AUDIENCE)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_AUDIENCE)]
    public string[] Audience { get; set; }

    [BsonElement(DB_KEY_DISCRIMINATOR)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_DISCRIMINATOR), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Discriminator { get; set; }

    [BsonElement(DB_KEY_EMAIL_ADDRESS), BsonIgnoreIfNull]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_EMAIL_ADDRESS), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Email { get; set; }

    [BsonElement(DB_KEY_EXPIRATION)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_EXPIRATION), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long Expiration { get; set; }

    [BsonElement(DB_KEY_IP_ADDRESS), BsonIgnoreIfNull]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_IP_ADDRESS), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string IpAddress { get; set; }

    [BsonElement(DB_KEY_COUNTRY_CODE), BsonIgnoreIfNull]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_COUNTRY_CODE), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string CountryCode { get; set; }

    [BsonElement(DB_KEY_ISSUED_AT)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_ISSUED_AT), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long IssuedAt { get; set; }

    [BsonElement(DB_KEY_ISSUER)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_ISSUER), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Issuer { get; set; }

    [BsonElement(DB_KEY_IS_ADMIN)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_IS_ADMIN), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsAdmin { get; set; }

    [BsonIgnore]
    [JsonIgnore]
    public bool IsNotAdmin => !IsAdmin;
    
    [BsonElement(DB_KEY_GAME)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_GAME)]
    public string GameKey { get; set; }
    
    [BsonElement(DB_KEY_REQUESTER)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_REQUESTER), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Requester { get; set; }

    [BsonElement(DB_KEY_SCREENNAME)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_SCREENNAME), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string ScreenName { get; set; }

    [BsonIgnore]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_SECONDS_REMAINING), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double SecondsRemaining { get; set; }

    [BsonIgnore]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_USERNAME), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Username => $"{ScreenName ?? "(Unknown Screenname)"}#{(Discriminator.ToString() ?? "").PadLeft(4, '0')}{(IsAdmin ? " (Administrator)" : "")}";

    [BsonIgnore]
    [JsonIgnore]
    public bool IsExpired => Expiration <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}