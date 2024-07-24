using System;
using System.Linq;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Models;

namespace Rumble.Platform.Common.Utilities;

public struct GeoBanInfo
{
    private static GeoBanInfo[] _info;
    private static string[] _codes;
    private static string[] _names;
    
    private string CountryCode { get; set; }
    private string[] CountryNames { get; set; }

    private GeoBanInfo(string code, params string[] names)
    {
        CountryCode = code;
        CountryNames = names;
    }
    
    // Per Eric, these are static values that come from Aristocrat security policies.
    // While we could potentially look them up dynamically, this suits our needs for the time being just fine,
    // and would be easy enough to change later as needed.
    private static GeoBanInfo[] BanList => _info ??= new GeoBanInfo[] 
    {
        new ("AF", "Afghanistan"),
        new ("BY", "Belarus"),
        new ("CN", "China", "People's Republic of China"),
        new ("IR", "Iran", "Islamic Republic of Iran"),
        new ("IQ", "Iraq"),
        new ("KP", "North Korea", "Democratic People's Republic of Korea"),
        new ("LB", "Lebanon"),
        new ("RU", "Russia", "Russian Federation"),
        new ("SS", "South Sudan"),
        new ("SD", "Sudan"),
        new ("SY", "Syria", "Syrian Arab Republic")
    };
    private static string[] BannedCountryCodes => _codes ??= BanList
        .Select(info => info.CountryCode)
        .ToArray();

    private static string[] BannedCountryNames => _names ??= BanList
        .SelectMany(info => info.CountryNames)
        .ToArray();

    public enum Status
    {
        Unknown = 0,
        AllClear = 1,
        SomeBanned = 2,
        AllBanned = 3
    }

    public static Status Validate(params string[] ipAddresses)
    {
        GeoIPData[] data = ipAddresses
            .Select(Interop.MaxMind.Lookup)
            .ToArray();
        
        string[] codes = data
            .Select(geo => geo.CountryCode)
            .Union(data.Select(geo => geo.RegisteredCountryCode))
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToArray();
        string[] countries = data
            .Select(geo => geo.Country)
            .Union(data.Select(geo => geo.RegisteredCountry))
            .Where(country => !string.IsNullOrWhiteSpace(country))
            .ToArray();

        if (codes.Length == 0 || countries.Length == 0)
            return Status.Unknown;
        
        if (codes.All(code => BannedCountryCodes.Contains(code)) || countries.All(country => BannedCountryNames.Contains(country)))
            return Status.AllBanned;

        // ReSharper disable once InvertIf
        if (codes.Any(code => BannedCountryCodes.Contains(code)) || countries.Any(country => BannedCountryNames.Contains(country)))
        {
            Log.Warn(Owner.Will, "When validating geobans on a set of IP addresses, some but not all were banned.", data: new
            {
                GeoData = data
            });
            return Status.SomeBanned;
        }

        return Status.AllClear;
    }
}