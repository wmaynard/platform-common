using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rumble.Platform.Common.Exceptions;

namespace Rumble.Platform.Common.Utilities
{
	public static class JsonHelper
	{
		public static JToken ValueFromToken(JObject json, string key, bool required = false)
		{
			JToken output = json[key];
			if (required && output == null)
				throw new FieldNotProvidedException(key);
			return output;
		}

		public static T Optional<T>(JObject json, string key)
		{
			try
			{
				return ValueFromToken(json, key).ToObject<T>();
			}
			catch (Exception)
			{
				return default;
			}
		}

		public static T Require<T>(JObject json, string key)
		{
			return ValueFromToken(json, key, required: true).ToObject<T>();
		}

		public static string RawJsonFrom(JObject json) => json.ToString(Formatting.None);
	}
}