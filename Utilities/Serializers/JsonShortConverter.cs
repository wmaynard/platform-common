using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rumble.Platform.Common.Exceptions;

namespace Rumble.Platform.Common.Utilities.Serializers;

[SuppressMessage("ReSharper", "PossibleNullReferenceException")]
[SuppressMessage("ReSharper", "SwitchStatementHandlesSomeKnownEnumValuesWithDefault")]
public class JsonShortConverter : JsonConverter<short>
{
	public override short Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
	{
		switch (reader.TokenType)
		{
			case JsonTokenType.String:
				string s = reader.GetString();
				return s.StartsWith('"') && s.EndsWith('"')
					? short.Parse(s[1..^1])
					: short.Parse(s);
			case JsonTokenType.Number:
				return reader.GetInt16();
			case JsonTokenType.True:
				return 1;
			case JsonTokenType.False:
			case JsonTokenType.Null:
				return 0;
			case JsonTokenType.None:
			default:
				throw new ConverterException("Unable to read short from JSON.", typeof(short), onDeserialize: true);
		}
	}

	public override void Write(Utf8JsonWriter writer, short value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}