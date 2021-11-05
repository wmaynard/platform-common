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
				new JsonExceptionConverter()
			}
		};

		public static JsonDocumentOptions DocumentOptions
		{
			get
			{
				if (_documentOptions.Equals(default(JsonDocumentOptions)))
					_documentOptions =new JsonDocumentOptions()
					{
						CommentHandling = JsonCommentHandling.Skip
					};
				return _documentOptions;
			}
		}
		public static T Optional<T>(JsonDocument json, string key) => Optional<T>(json.RootElement, key);
		public static T Optional<T>(JsonElement json, string key)
		{
			return json.TryGetProperty(key, out JsonElement value) 
				? JsonSerializer.Deserialize<T>(value.GetRawText()) 
				: default;
		}

		public static T Require<T>(JsonDocument json, string key) => Require<T>(json.RootElement, key);
		public static T Require<T>(JsonElement json, string key) => JsonSerializer.Deserialize<T>(json.GetProperty(key).GetRawText());
	}
}