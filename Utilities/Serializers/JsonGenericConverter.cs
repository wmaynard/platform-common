using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using Rumble.Platform.Common.Exceptions;
using JsonTokenType = System.Text.Json.JsonTokenType;

namespace Rumble.Platform.Common.Utilities.Serializers
{
	public class JsonGenericConverter : JsonConverter<GenericData>
	{
		#region READ
		public override GenericData Read(ref Utf8JsonReader reader, Type data, JsonSerializerOptions options)
		{
			try
			{
				return ReadGeneric(ref reader);
			}
			catch (Exception e)
			{
				throw new ConverterException(e.Message, typeof(GenericData), e, onDeserialize: true);
			}
		}

		/// <summary>
		/// Reads a GenericData object from an endpoint's payload or other raw JSON.
		/// </summary>
		[SuppressMessage("ReSharper", "AssignNullToNotNullAttribute")]
		private static GenericData ReadGeneric(ref Utf8JsonReader reader)
		{
			GenericData output = new GenericData();
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
						output[key] = ReadGeneric(ref reader);
						break;
					case JsonTokenType.EndObject:
						return output;
					case JsonTokenType.StartArray:
						output[key] = ReadArray(ref reader);
						break;
					case JsonTokenType.Number:
						if (!reader.TryGetDecimal(out decimal asDecimal))
							throw new ConverterException("Couldn't parse number.", typeof(GenericData), onDeserialize: true);
						output[key] = asDecimal;
						break;
					case JsonTokenType.EndArray:
					default:
						throw new ConverterException("Operation should be impossible.", typeof(GenericData), onDeserialize: true);
				}

			return null;
		}
		
		/// <summary>
		/// Reads an array for a GenericData object.  Arrays require special handling since they do not have field names.
		/// </summary>
		private static List<object> ReadArray(ref Utf8JsonReader reader)
		{
			List<object> output = new List<object>();
			while (reader.Read())
				switch (reader.TokenType)
				{
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
						output.Add(ReadGeneric(ref reader));
						break;
					case JsonTokenType.StartArray:
						output.Add(ReadArray(ref reader));
						break;
					case JsonTokenType.EndArray:
						return output;
					case JsonTokenType.Number:
						if (!reader.TryGetDecimal(out decimal asDecimal))
							throw new ConverterException("Couldn't parse number.", typeof(GenericData), onDeserialize: true);
						output.Add(asDecimal);
						break;
					case JsonTokenType.PropertyName:
					case JsonTokenType.EndObject:
					default:
						throw new ConverterException("Operation should be impossible.", typeof(GenericData), onDeserialize: true);
				}

			return output;
		}
		#endregion READ

		#region WRITE
		public override void Write(Utf8JsonWriter writer, GenericData value, JsonSerializerOptions options)
		{
			WriteJson(ref writer, options, ref value);
		}

		/// <summary>
		/// Writes a GenericData object to JSON for a response body.
		/// </summary>
		private void WriteJson(ref Utf8JsonWriter writer, JsonSerializerOptions options, ref GenericData value)
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
						WriteJsonArray(ref writer, ref asEnumerable, options);
						break;
					case GenericData asGeneric:
						writer.WritePropertyName(key);
						Write(writer, asGeneric, options);
						break;
					case null:
						writer.WriteNull(key);
						break;
					default:
						throw new ConverterException("Unexpected data type.", kvp.Value.GetType());
				}
			}
			writer.WriteEndObject();
		}

		/// <summary>
		/// Writes an array from a GenericData object.  Arrays require special handling since they do not have field names.
		/// </summary>
		private void WriteJsonArray(ref Utf8JsonWriter writer, ref IEnumerable<object> value, JsonSerializerOptions options)
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
						if (asDecimal.ToString(CultureInfo.InvariantCulture).Contains('.'))
							writer.WriteNumberValue((double)asDecimal);
						else
							writer.WriteNumberValue((long)asDecimal);
						break;
					case IEnumerable<object> asArray:
						WriteJsonArray(ref writer, ref asArray, options);
						break;
					case GenericData asGeneric:
						Write(writer, asGeneric, options);
						break;
					case null:
						writer.WriteNullValue();
						break;
					default:
						throw new ConverterException("Unexpected data type.", obj.GetType());
				}
			writer.WriteEndArray();
		}
		#endregion WRITE
	}
}