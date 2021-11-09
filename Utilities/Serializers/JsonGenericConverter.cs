using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using JsonTokenType = System.Text.Json.JsonTokenType;

namespace Rumble.Platform.Common.Utilities.Serializers
{

	
	public class JsonGenericConverter : JsonConverter<GenericJSON>
	{
		public override GenericJSON Read(ref Utf8JsonReader reader, Type data, JsonSerializerOptions options)
		{
			try
			{
				return Extract(ref reader);
			}
			catch (Exception e)
			{
				Log.Error(Owner.Default, "Could not deserialize JSON into a GenericJSON");
				throw;
			}
		}

		private static List<object> ExtractToList(ref Utf8JsonReader reader)
		{
			List<object> output = new List<object>();
			while (reader.Read())
				switch (reader.TokenType)
				{
					case JsonTokenType.PropertyName:
					case JsonTokenType.EndObject:
						throw new Exception("This should be impossible.");
					case JsonTokenType.True:
					case JsonTokenType.False:
						output.Add(reader.GetBoolean());
						break;
					case JsonTokenType.Null:
					case JsonTokenType.None:
					case JsonTokenType.Comment:
						output.Add(null);
						break;
					case JsonTokenType.String:
						output.Add(reader.GetString());
						break;
					case JsonTokenType.StartObject:
						output.Add(Extract(ref reader));
						break;
					case JsonTokenType.StartArray:
						output.Add(ExtractToList(ref reader));
						break;
					case JsonTokenType.EndArray:
						return output;
					case JsonTokenType.Number:
						if (!reader.TryGetDecimal(out decimal asDecimal))
							throw new Exception("Couldn't parse number.");
						output.Add(asDecimal);
						break;
					default:
						throw new ArgumentOutOfRangeException();
				}

			return output;
		}

		private static GenericJSON Extract(ref Utf8JsonReader reader)
		{
			GenericJSON output = new GenericJSON();
			string key = null;
			while (reader.Read())
				switch (reader.TokenType)
				{
					case JsonTokenType.PropertyName:
						key = reader.GetString();
						break;
					case JsonTokenType.True:
					case JsonTokenType.False:
						output[key] = reader.GetBoolean();
						break;
					case JsonTokenType.Null:
					case JsonTokenType.None:
					case JsonTokenType.Comment:
						output[key] = null;
						break;
					case JsonTokenType.String:
						output[key] = reader.GetString();
						break;
					case JsonTokenType.StartObject:
						output[key] = Extract(ref reader);
						break;
					case JsonTokenType.EndObject:
						return output;
					case JsonTokenType.StartArray:
						output[key] = ExtractToList(ref reader);
						break;
					case JsonTokenType.EndArray:
						throw new Exception("This should be impossible.");
					case JsonTokenType.Number:
						if (!reader.TryGetDecimal(out decimal asDecimal))
							throw new Exception("Couldn't parse number.");
						output[key] = asDecimal;
						break;
					default:
						throw new ArgumentOutOfRangeException();
			}

			return null;
		}

		public override void Write(Utf8JsonWriter writer, GenericJSON value, JsonSerializerOptions options)
		{
			writer.WriteStartObject();

			foreach (KeyValuePair<string, object> kvp in value)
			{
				string key = kvp.Key;
				switch (kvp.Value)
				{
					case bool asBool:
						writer.WriteBoolean(key, asBool);
						break;
					case string asString:
						writer.WriteString(key, asString);
						break;
					case decimal asDecimal:
						writer.WriteNumber(key, asDecimal);
						break;
					case IEnumerable<object> asEnumerable:
						writer.WritePropertyName(key);
						W(ref writer, asEnumerable, options);
						break;
					case GenericJSON asGeneric:
						Write(writer, asGeneric, options);
						break;
					case null:
						writer.WriteNull(key);
						break;
					default:
						throw new NotImplementedException();
				}
			}
			writer.WriteEndObject();
		}

		private void W(ref Utf8JsonWriter writer, IEnumerable<object> value, JsonSerializerOptions options)
		{
			writer.WriteStartArray();
			foreach (object obj in value)
				switch (obj)
				{
					case bool asBool:
						writer.WriteBooleanValue(asBool);
						break;
					case string asString:
						writer.WriteStringValue(asString);
						break;
					case decimal asDecimal:
						if (asDecimal.ToString().Contains('.'))
							writer.WriteNumberValue((double)asDecimal);
						else
							writer.WriteNumberValue((long)asDecimal);
						break;
					case IEnumerable<object> asArray:
						W(ref writer, asArray, options);
						break;
					case GenericJSON asGeneric:
						// writer.WriteStartObject();
						// writer.WriteString("whoever", "this is dumb");
						// writer.WriteEndObject();
						Write(writer, asGeneric, options);
						break;
					case null:
						writer.WriteNullValue();
						break;
					default:
						throw new NotImplementedException();
				}
			writer.WriteEndArray();
		}
	}
}