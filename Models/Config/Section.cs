using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Models.Config;

public class Section : PlatformCollectionDocument
{
    public const string DB_KEY_SERVICES = "svc";
    public const string DB_KEY_VALUES = "v";
    public const string DB_KEY_ADMIN_TOKEN = "token";
    public const string DB_KEY_NAME = "service";
    public const string DB_KEY_FRIENDLY_NAME = "name";

    public const string FRIENDLY_KEY_SERVICES = "registeredServices";
    public const string FRIENDLY_KEY_ADMIN_VALUES = "commentedValues";
    public const string FRIENDLY_KEY_VALUES = "values";
    public const string FRIENDLY_KEY_ADMIN_TOKEN = "adminToken";
    public const string FRIENDLY_KEY_NAME = "serviceName";
    public const string FRIENDLY_KEY_FRIENDLY_NAME = "friendlyName";

    [BsonElement(DB_KEY_SERVICES)] // TODO: Allow SimpleIndex to enforce unique constraints?
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_SERVICES), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<RegisteredService> Services { get; set; }
  
    [BsonElement(DB_KEY_VALUES)]
    [JsonInclude, JsonPropertyName("data"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, SettingsValue> Data { get; set; }

    [BsonIgnore]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_ADMIN_VALUES), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RumbleJson AdminData => RumbleJson.FromDictionary(Data);

    [BsonIgnore]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_VALUES), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RumbleJson ClientData => RumbleJson.FromDictionary(Data.ToDictionary(keySelector: pair => pair.Key, elementSelector: pair => pair.Value.Value));
  
    [BsonElement(DB_KEY_ADMIN_TOKEN)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_ADMIN_TOKEN), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string AdminToken { get; init; }
  
    [BsonElement(DB_KEY_NAME)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_NAME)]
    public string Name { get; set; }
  
    [BsonElement(DB_KEY_FRIENDLY_NAME)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_FRIENDLY_NAME)]
    public string FriendlyName { get; set; }
  
    public List<DynamicConfig.DC2ClientInformation> ActiveClients { get; set; }

    [JsonConstructor, BsonConstructor]
    public Section(){}

    public Section(string id) => Id = id;

    public Section(string name, string friendlyName)
    {
        Name = name;
        FriendlyName = friendlyName; 
        Data = new Dictionary<string, SettingsValue>();
        Services = new List<RegisteredService>();
        AdminToken = null;
        ActiveClients = new List<DynamicConfig.DC2ClientInformation>();
    }

    public void ResetId() => Id = null;

    public Section PrepareForExport()
    {
        Services = new List<RegisteredService>();
        ActiveClients = new List<DynamicConfig.DC2ClientInformation>();
        return this;
    }

    [BsonIgnore]
    [JsonIgnore]
    public string[] AllKeys => Data
        .Select(json => $"{Name}.{json.Key}")
        .ToArray();

    public static Dictionary<string, string> Flatten(IEnumerable<Section> sections)
    {
        var x  = sections
            .Select(section => section.Data
                .ToDictionary(
                    keySelector: pair => $"{section.Name}.{pair.Key}",
                    elementSelector: pair => pair.Value.Value
                )
            );

        Dictionary<string, string> output = new Dictionary<string, string>();
        foreach (Dictionary<string, string> dict in x)
        {
            foreach (string key in dict.Keys)
                output[key] = dict[key];
        }

        return output;
    }

    /// <summary>
    /// Flattens a config into some simple tuples for easy Diff manipulation.
    /// Item1: The environment the config is for.
    /// Item2: {service name}.{key}
    /// Item3: {value}
    /// </summary>
    /// <param name="environment"></param>
    /// <param name="sections"></param>
    /// <returns></returns>
    private static List<Tuple<string, string, string>> Flatten(string environment, IEnumerable<Section> sections) => sections
        .SelectMany(section => section.Data.Select(setting => new Tuple<string, string, string>(
            item1: environment,
            item2: $"{section.Name}.{setting.Key}",
            item3: setting.Value.Value
        )))
        .ToList();

    /// <summary>
    /// Turn tuples into an array of Diffs.  This filters out items that are
    /// </summary>
    /// <param name="urls"></param>
    /// <param name="list"></param>
    /// <returns></returns>
    private static Diff[] DiffsFromTuples(List<string> urls, List<Tuple<string, string, string>> list)
    {
        list = list.OrderBy(tuple => tuple.Item2).ToList();
        string[] allKeys = list
            .Select(tuple => tuple.Item2)
            .Distinct()
            .ToArray();
        
        foreach (string key in allKeys)
        {
            string[] missing = urls
                .Except(list
                    .Where(tuple => tuple.Item2 == key)
                    .Select(tuple => tuple.Item1)
                )
                .ToArray();
            bool uniform = list
                .Where(tuple => tuple.Item2 == key)
                .Select(tuple => tuple.Item3)
                .Distinct()
                .Count() == 1;

            if (missing.Any())
                list.AddRange(missing.Select(str => new Tuple<string, string, string>(
                    item1: str,
                    item2: key,
                    item3: "THIS VALUE IS MISSING."
                )));
            else if (uniform)
                list.RemoveAll(tuple => tuple.Item2 == key);
        }

        return list
            .GroupBy(tuple => tuple.Item2)
            .Where(group => !group.Key.EndsWith(".adminToken")) // Omit admin tokens for security reasons / unnecessary.
            .Select(group => new Diff
            {
                Key = group.Key,
                Data = group
                    .Select(tuple => new DiffValue
                    {
                        Environment = tuple.Item1,
                        Value = tuple.Item3
                    })
                    .ToArray()
            })
            .ToArray();
    }

    /// <summary>
    /// Takes a dictionary of environment / sections and returns a diff of all of them.  The dictionary keys
    /// are environment URLs; the values array is the entire config for that environment.
    /// The returned data is sorted by {service-name}.{config key}, ascending.  Items that are uniform and present
    /// on all specified environments are omitted.
    /// </summary>
    /// <param name="dict"></param>
    /// <param name="limiter">Adds a service filter, if desired, to return a partial diff.</param>
    /// <returns></returns>
    public static Diff[] GetDiff(Dictionary<string, Section[]> dict, string limiter = null)
    {
        List<Tuple<string, string, string>> flattened = new List<Tuple<string, string, string>>();
        
        foreach (KeyValuePair<string, Section[]> pair in dict)
            flattened.AddRange(Flatten(pair.Key, pair.Value));

        Diff[] output = DiffsFromTuples(dict.Keys.ToList(), flattened);

        return limiter == null
            ? output
            : output.Where(diff => diff.Key.StartsWith(limiter)).ToArray();
    }
    
    public class Diff : PlatformDataModel
    {
        public string Key { get; set; }
        public DiffValue[] Data { get; set; }
    }
    
    public class DiffValue : PlatformDataModel
    {
        public string Environment { get; set; }
        public string Value { get; set; }
    }
}



