using System;
using System.Text.Json;
using System.Xml;
using MongoDB.Driver;
using Rumble.Platform.Common.Exceptions;


namespace Rumble.Platform.Common.Utilities
{
	public static class JsonHelper
	{
		public static T Optional<T>(JsonDocument json, string key) => Optional<T>(json.RootElement, key);
		public static T Optional<T>(JsonElement json, string key)
		{
			return json.TryGetProperty(key, out JsonElement value) 
				? JsonSerializer.Deserialize<T>(value.GetRawText()) 
				: default;
		}

		public static T Require<T>(JsonDocument json, string key) => Require<T>(json.RootElement, key);
		public static T Require<T>(JsonElement json, string key) => JsonSerializer.Deserialize<T>(json.GetProperty(key).GetRawText());


		//
		//
		// public static JToken ValueFromToken(JObject json, string key, bool required = false)
		// {
		// 	JToken output = json[key];
		// 	if (required && output == null)
		// 		throw new FieldNotProvidedException(key);
		// 	return output;
		// }
		//
		// public static JToken ValueFromToken(JToken json, string key, bool required = false)
		// {
		// 	JToken output = json[key];
		// 	if (required && output == null)
		// 		throw new FieldNotProvidedException(key);
		// 	return output;
		// }
		//
		// public static T Optional<T>(JToken json, string key)
		// {
		// 	try
		// 	{
		// 		return ValueFromToken(json, key).ToObject<T>();
		// 	}
		// 	catch (Exception e)
		// 	{
		// 		Log.Local(Owner.Default, $"Unable to parse {key} from JSON.");
		// 		return default;
		// 	}
		// }
		// public static T Optional<T>(JObject json, string key)
		// {
		// 	try
		// 	{
		// 		return ValueFromToken(json, key).ToObject<T>();
		// 	}
		// 	catch (Exception)
		// 	{
		// 		Log.Local(Owner.Default, $"Unable to parse {key} from JSON.");
		// 		return default;
		// 	}
		// }
		//
		// public static T Require<T>(JToken json, string key) => ValueFromToken(json, key, true).ToObject<T>();
		// public static T Require<T>(JObject json, string key) => ValueFromToken(json, key, required: true).ToObject<T>();
		//
		// public static string RawJsonFrom(JObject json) => json.ToString(Formatting.None);
	}
}