using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Web;
using JsonTokenType = System.Text.Json.JsonTokenType;

namespace Rumble.Platform.Common.Utilities.Serializers;

public class JsonGenericConverter : JsonConverter<GenericData>
{
  // Debug helpers used with below kluge.
  private static string Debug(ReadOnlySequence<byte> seq) => string.Join("", seq.ToArray().Select(s => (char)s));
  private static string Debug(ReadOnlySpan<byte> seq) => string.Join("", seq.ToArray().Select(s => (char)s));
  #region READ
  public override GenericData Read(ref Utf8JsonReader reader, Type data, JsonSerializerOptions options)
  {
    try
    {
      // TODO: Identify the root cause of this problem and clean up this kluge.
      // Will on 2021.01.14: This addresses a very specific scenario:
      //   * You are deserializing a PlatformDataModel.
      //   * That Model has a GenericData field.
      //   * That GenericData field is coming from stringified JSON.
      // Something is causing the reader to start at the *end* of the stringified JSON.  When this happens,
      // the GenericData field is skipped entirely, and the reader moves on to a token it shouldn't be accessing,
      // resulting in an Exception later on, which subsequently yields a null GenericData.
      // This recursive call to deserialize the stringified JSON seems to work, but it's dangerous, janky, and hard to understand.
      if (reader.TokenType == JsonTokenType.String)
        try { return reader.GetString(); }
        catch { }
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
    {
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
          if (key == null)
            throw new Exception();
          output[key] = asDecimal;
          break;
        case JsonTokenType.EndArray:
        default:
          throw new ConverterException("Operation should be impossible.", typeof(GenericData), onDeserialize: true);
      }
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
  public override void Write(Utf8JsonWriter writer, GenericData value, JsonSerializerOptions options) => WriteJson(ref writer, options, ref value);

  /// <summary>
  /// Writes a GenericData object to JSON for a response body.
  /// </summary>
  private void WriteJson(ref Utf8JsonWriter writer, JsonSerializerOptions options, ref GenericData value)
  {
    writer.WriteStartObject();

    if (value == null)
    {
      Log.Error(Owner.Will, "Unexpected end of JSON.  Something may not have serialized correctly.");
      writer.WriteEndObject();
      return;
    }
      
    foreach (KeyValuePair<string, object> pair in value)
    {
      string key = pair.Key;
      switch (pair.Value)
      {
        case bool asBool:
          writer.WriteBoolean(key, asBool);
          break;
        case Enum asEnum:
          writer.WriteString(key, asEnum.ToString());
          break;
        case string asString:
          writer.WriteString(key, asString);
          break;
        case short asShort:
          writer.WriteNumber(key, asShort);
          break;
        case int asInt:
          writer.WriteNumber(key, asInt);
          break;
        case long asLong:
          writer.WriteNumber(key, asLong);
          break;
        case float asFloat:
          writer.WriteNumber(key, asFloat);
          break;
        case double asDouble:
          writer.WriteNumber(key, asDouble);
          break;
        case decimal asDecimal:
          writer.WriteNumber(key, asDecimal);
          break;
        
        case GenericData asGeneric:
          writer.WritePropertyName(key);
          Write(writer, asGeneric, options);
          break;
        case IEnumerable asEnumerable:
          writer.WritePropertyName(key);
          WriteJsonArray(ref writer, ref asEnumerable, options);
          break;
        case null:
          writer.WriteNull(key);
          break;
        case PlatformDataModel asModel:
          writer.WritePropertyName(key);
          Write(writer, asModel.JSON, options);
          break;
        default: // TODO: Anonymous type throws an error on this
          Log.Warn(Owner.Default, "Unexpected data type during GenericData serialization.", data: new
          {
            Information = "A custom data type was likely passed into a GenericData object and JSON may not have serialized as expected.",
            DataType = pair.Value.GetType()
          });
          writer.WritePropertyName(key);
          Write(writer, JsonSerializer.Serialize(pair.Value, options), options);
          break;
      }
    }
    writer.WriteEndObject();
  }

  /// <summary>
  /// Writes an array from a GenericData object.  Arrays require special handling since they do not have field names.
  /// </summary>
  private void WriteJsonArray(ref Utf8JsonWriter writer, ref IEnumerable value, JsonSerializerOptions options)
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
        case int asInt:
          writer.WriteNumberValue(asInt);
          break;
        case long asLong:
          writer.WriteNumberValue(asLong);
          break;
        case float asFloat:
          writer.WriteNumberValue(asFloat);
          break;
        case decimal asDecimal:
          if (asDecimal.ToString(CultureInfo.InvariantCulture).Contains('.'))
            writer.WriteNumberValue((double)asDecimal);
          else
            writer.WriteNumberValue((long)asDecimal);
          break;
        case GenericData asGeneric:
          Write(writer, asGeneric, options);
          break;
        // case KeyValuePair<string, object> asPair:
        //  Write(writer, new GenericData{{asPair.Key, asPair.Value}}, options);
        //  break;
        case IEnumerable asArray:
          WriteJsonArray(ref writer, ref asArray, options);
          break;
        case Enum asEnum:
          writer.WriteStringValue(asEnum.ToString());
          break;
        case PlatformDataModel asModel:
          Write(writer, asModel.JSON, options);
          break;
        case null:
          writer.WriteNullValue();
          break;
        default:
          try
          {
            Type type = obj.GetType();
            if (type != null && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
              dynamic dynamo = obj;
              Write(writer, new GenericData{{dynamo.Key, dynamo.Value}}, options);
              break;
            }
          }
          catch { }
          
          if (PlatformEnvironment.IsLocal)
            Log.Warn(Owner.Default, "Unexpected data type during GenericData serialization.", data: new
            {
              Information = "A custom data type was likely passed into a GenericData object and JSON may not have serialized as expected.",
              DataType = obj.GetType()
            });
          Write(writer, JsonSerializer.Serialize(obj, options), options);
          break;
      }
    writer.WriteEndArray();
  }
  #endregion WRITE
}