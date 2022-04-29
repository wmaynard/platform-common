using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rumble.Platform.Common.Exceptions;

namespace Rumble.Platform.Common.Utilities.Serializers;

[SuppressMessage("ReSharper", "PossibleNullReferenceException")]
[SuppressMessage("ReSharper", "SwitchStatementHandlesSomeKnownEnumValuesWithDefault")]
public class JsonLongConverter : JsonConverter<long>
{
	public override long Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
	{
		switch (reader.TokenType)
		{
			case JsonTokenType.String:
				string s = reader.GetString();
				return s.StartsWith('"') && s.EndsWith('"')
					? long.Parse(s[1..^1])
					: long.Parse(s);
			case JsonTokenType.Number:
				return reader.GetInt64();
			case JsonTokenType.True:
				return 1;
			case JsonTokenType.False:
			case JsonTokenType.Null:
				return 0;
			case JsonTokenType.None:
			default:
				throw new ConverterException("Unable to read long from JSON.", typeof(long), onDeserialize: true);
		}
	}

	public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}