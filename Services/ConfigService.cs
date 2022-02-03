using System.Collections.Generic;
using System.Linq;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.Common.Services
{
	/// <summary>
	/// Useful when a service needs to store configuration values specific to itself between runs.
	/// This service uses its own MongoDB collection to store GenericData values.
	/// </summary>
	public sealed class ConfigService : PlatformMongoService<ConfigService.ServiceConfig>
	{
		private ServiceConfig _config;
		private ServiceConfig Config => _config				// The config has been loaded before this session.
			??= Find(config => true).FirstOrDefault()	// The config has not yet been loaded.  Find it.
			?? Create(new ServiceConfig());			// No config has been created yet.  Do so now.

		public T Value<T>(string key) => Config.Data.Optional<T>(key);
		private object Value(string key) => Config.Data.Optional(key);

		public void Update(string key, object data)
		{
			bool changed = data != null && !data.Equals(Value(key));
			Config.Data[key] = data;
			if (changed)
				Update(Config); // TODO: UpdateAsync fire and forget
		}
		
		public ConfigService() : base("serviceConfig") { }

		public class ServiceConfig : PlatformCollectionDocument
		{
			public GenericData Data { get; set; }
			internal ServiceConfig() => Data = new GenericData();
		}
	}
}