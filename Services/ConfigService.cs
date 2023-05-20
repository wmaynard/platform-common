using System.Collections.Generic;
using System.Linq;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Data;

namespace Rumble.Platform.Common.Services;

/// <summary>
/// Useful when a service needs to store configuration values specific to itself between runs.
/// This service uses its own MongoDB collection to store RumbleJson values.
/// </summary>
public sealed class ConfigService : PlatformMongoService<ConfigService.ServiceConfig>
{
    private ServiceConfig _config;
    private ServiceConfig Config => _config ?? Refresh();
    public static ConfigService Instance { get; private set; }

    public T Value<T>(string key) => Config.Data.Optional<T>(key);
    private object Value(string key) => Config.Data.Optional(key);

    public T Require<T>(string key) => Config.Data.Require<T>(key);
    public T Optional<T>(string key) => Config.Data.Optional<T>(key);
    public void Set(string key, object data) => Update(key, data);

    public void Update(string key, object data)
    {
        bool changed = data != null && !data.Equals(Value(key));
        Config.Data[key] = data;
        if (changed)
            Update(Config); // TODO: UpdateAsync fire and forget
    }
    public ServiceConfig Refresh() => _config = 
        Find(config => true).FirstOrDefault() 
        ?? Create(new ServiceConfig());

    public ConfigService() : base("config")
    {
        Refresh();
        Instance = this;
    } 

    public class ServiceConfig : PlatformCollectionDocument
    {
        public RumbleJson Data { get; set; }
        internal ServiceConfig() => Data = new RumbleJson();
    }
}