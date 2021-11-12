using System;
using System.Text.Json;
using System.Xml;
using MongoDB.Driver;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities.Serializers;


namespace Rumble.Platform.Common.Utilities
{
	public static class JsonHelper
	{
		private static JsonSerializerOptions _serializerOptions;
		private static JsonDocumentOptions _documentOptions;

		public static JsonSerializerOptions SerializerOptions => _serializerOptions ??= new JsonSerializerOptions()
		{
			IgnoreNullValues = false,
			IncludeFields = true,
			IgnoreReadOnlyFields = false,
			IgnoreReadOnlyProperties = false,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			ReadCommentHandling = JsonCommentHandling.Skip,
			Converters =
			{
				new JsonTypeConverter(),
				new JsonExceptionConverter(),
				new JsonGenericConverter()
			}
		};

		public static JsonDocumentOptions DocumentOptions
		{
			get
			{
				if (_documentOptions.Equals(default(JsonDocumentOptions)))
					_documentOptions = new JsonDocumentOptions()
					{
						CommentHandling = JsonCommentHandling.Skip
					};
				return _documentOptions;
			}
		}

		public static JsonElement Optional(JsonDocument json, string key) => Optional(json.RootElement, key);
		public static JsonElement Optional(JsonElement json, string key)
		{
			return json.TryGetProperty(key, out JsonElement value)
				? value
				: default;
		}
		public static T Optional<T>(JsonDocument json, string key) => Optional<T>(json.RootElement, key);
		public static T Optional<T>(JsonElement json, string key)
		{
			return json.TryGetProperty(key, out JsonElement value) 
				? JsonSerializer.Deserialize<T>(value.GetRawText(), SerializerOptions) 
				: default;
		}

		public static JsonElement Require(JsonDocument json, string key) => Require(json.RootElement, key);
		public static JsonElement Require(JsonElement json, string key) => json.GetProperty(key);
		public static T Require<T>(JsonDocument json, string key) => Require<T>(json.RootElement, key);

		public static T Require<T>(JsonElement json, string key)
		{
			JsonElement element = default;
			try
			{
				element = json.GetProperty(key);
				return JsonSerializer.Deserialize<T>(element.GetRawText(), SerializerOptions);
			}
			catch (Exception e)
			{
				Log.Info(Owner.Default, $"Unable to deserialize JSON '{key}'.", data: new
				{
					Element = element,
					AttemptedType = typeof(T).Name
				}, exception: e);
				throw;
			}
		}
	}
}