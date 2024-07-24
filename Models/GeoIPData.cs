using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Utilities.JsonTools;

namespace Rumble.Platform.Common.Models;

public class GeoIPData : PlatformDataModel
{
    // Note: these are all built for MaxMind and probably won't work if we use a different provider.
    internal const string KEY_CONTINENT = "continent";
    internal const string KEY_COUNTRY = "country";
    internal const string KEY_REGISTERED_COUNTRY = "registered_country";
    internal const string KEY_ENGLISH = "en";
    internal const string KEY_NAMES = "names";
    internal const string KEY_CODE = "code";
    internal const string KEY_ISO_CODE = "iso_code";
    private RumbleJson Data { get; init; }

    [BsonIgnore]
    public string Continent { get; init; }
    
    [BsonIgnore]
    public string ContinentCode { get; init; }
    
    [BsonElement("nat")]
    public string Country { get; init; }
    
    [BsonElement("cc")]
    public string CountryCode { get; init; }

    [BsonIgnore]
    public string RegisteredCountry { get; init; }
    
    [BsonIgnore]
    public string RegisteredCountryCode { get; init; }

    [BsonElement("ip")]
    public string IPAddress { get; init; }

    internal GeoIPData(string ipAddress, RumbleJson data = null)
    {
        IPAddress = ipAddress;
        Data = data;

        RumbleJson continent = Data?.Optional<RumbleJson>(KEY_CONTINENT);
        RumbleJson country = Data?.Optional<RumbleJson>(KEY_COUNTRY);
        RumbleJson registered = Data?.Optional<RumbleJson>(KEY_REGISTERED_COUNTRY);

        ContinentCode = continent?.Optional<string>(KEY_CODE);
        Continent = continent
            ?.Optional<RumbleJson>(KEY_NAMES)
            ?.Optional<string>(KEY_ENGLISH);

        CountryCode = country?.Optional<string>(KEY_ISO_CODE);
        Country = country
            ?.Optional<RumbleJson>(KEY_NAMES)
            ?.Optional<string>(KEY_ENGLISH);

        RegisteredCountryCode = registered?.Optional<string>(KEY_CODE);
        RegisteredCountry = registered
            ?.Optional<RumbleJson>(KEY_NAMES)
            ?.Optional<string>(KEY_ENGLISH);
    }

    private static GeoIPData FromMaxMind(string ipAddress) => Interop.MaxMind.Lookup(ipAddress);

    // Keep MaxMind-specific interop limited to this class; don't pollute other classes with it.
    // If we switch IP lookup providers, this keeps our code consistent.
    internal static GeoIPData FromAddress(string ip) => FromMaxMind(ip);
}