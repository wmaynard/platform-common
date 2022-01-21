using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Models
{
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
		private GenericData Data { get; init; }
		
		public string Continent { get; init; }
		public string ContinentCode { get; init; }
		
		public string Country { get; init; }
		public string CountryCode { get; init; }
		
		public string RegisteredCountry { get; init; }
		public string RegisteredCountryCode { get; init; }
		
		public string IPAddress { get; init; }

		internal GeoIPData(string ipAddress, GenericData data = null)
		{
			IPAddress = ipAddress;
			Data = data;

			GenericData continent = Data?.Optional<GenericData>(KEY_CONTINENT);
			GenericData country = Data?.Optional<GenericData>(KEY_COUNTRY);
			GenericData registered = Data?.Optional<GenericData>(KEY_REGISTERED_COUNTRY);
			
			ContinentCode = continent?.Optional<string>(KEY_CODE);
			Continent = continent
				?.Optional<GenericData>(KEY_NAMES)
				?.Optional<string>(KEY_ENGLISH);
			
			CountryCode = country?.Optional<string>(KEY_ISO_CODE);
			Country = country
				?.Optional<GenericData>(KEY_NAMES)
				?.Optional<string>(KEY_ENGLISH);
			
			RegisteredCountryCode = registered?.Optional<string>(KEY_CODE);
			RegisteredCountry = registered
				?.Optional<GenericData>(KEY_NAMES)
				?.Optional<string>(KEY_ENGLISH);
		}

		private static GeoIPData FromMaxMind(string ipAddress) => Interop.MaxMind.Lookup(ipAddress);

		// Keep MaxMind-specific interop limited to this class; don't pollute other classes with it.
		// If we switch IP lookup providers, this keeps our code consistent.
		internal static GeoIPData FromAddress(string ip) => FromMaxMind(ip);
	}
}