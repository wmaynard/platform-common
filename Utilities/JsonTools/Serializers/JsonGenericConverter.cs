using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;
using Rumble.Platform.Common.Utilities.JsonTools.Exceptions;
using Rumble.Platform.Common.Utilities.JsonTools.Utilities;
using JsonTokenType = System.Text.Json.JsonTokenType;

namespace Rumble.Platform.Common.Utilities.JsonTools.Serializers;

public class JsonGenericConverter : JsonConverter<RumbleJson>
{
    // Debug helpers used with below kluge.
    private static string Debug(ReadOnlySequence<byte> seq) => string.Join("", seq.ToArray().Select(s => (char)s));
    private static string Debug(ReadOnlySpan<byte> seq) => string.Join("", seq.ToArray().Select(s => (char)s));
    #region READ
    public override RumbleJson Read(ref Utf8JsonReader reader, Type data, JsonSerializerOptions options)
    {
        try
        {
            // TODO: Identify the root cause of this problem and clean up this kluge.
            // Will on 2021.01.14: This addresses a very specific scenario:
            //   * You are deserializing a PlatformDataModel.
            //   * That Model has a RumbleJson field.
            //   * That RumbleJson field is coming from stringified JSON.
            // Something is causing the reader to start at the *end* of the stringified JSON.  When this happens,
            // the RumbleJson field is skipped entirely, and the reader moves on to a token it shouldn't be accessing,
            // resulting in an Exception later on, which subsequently yields a null RumbleJson.
            // This recursive call to deserialize the stringified JSON seems to work, but it's dangerous, janky, and hard to understand.
            if (reader.TokenType == JsonTokenType.String)
                try { return reader.GetString(); }
                catch { }
            return ReadGeneric(ref reader);
        }
        catch (Exception e)
        {
            return Throw.Ex<RumbleJson>(new ConverterException(e.Message, typeof(RumbleJson), e, onDeserialize: true));
        }
    }

    /// <summary>
    /// Reads a RumbleJson object from an endpoint's payload or other raw JSON.
    /// </summary>
    [SuppressMessage("ReSharper", "AssignNullToNotNullAttribute")]
    private static RumbleJson ReadGeneric(ref Utf8JsonReader reader)
    {
        RumbleJson output = new();
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
                    output[key] = RumbleJson.SanitizeStringsOnDeserialize
                        ? reader.GetString()?.Trim()
                        : reader.GetString();
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
                        return Throw.Ex<RumbleJson>(new ConverterException("Couldn't parse number.", typeof(RumbleJson), onDeserialize: true));
                    if (key == null)
                        return Throw.Ex<RumbleJson>(new Exception("Key was null."));
                    output[key] = asDecimal;
                    break;
                case JsonTokenType.EndArray:
                default:
                    return Throw.Ex<RumbleJson>(new ConverterException("Operation should be impossible.", typeof(RumbleJson), onDeserialize: true));
            }
        return null;
    }

    /// <summary>
    /// Reads an array for a RumbleJson object.  Arrays require special handling since they do not have field names.
    /// </summary>
    private static List<object> ReadArray(ref Utf8JsonReader reader)
    {
        List<object> output = new();
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
                    output.Add(RumbleJson.SanitizeStringsOnDeserialize
                        ? reader.GetString()?.Trim()
                        : reader.GetString()
                    );
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
                        return Throw.Ex<List<object>>(new ConverterException("Couldn't parse number.", typeof(RumbleJson), onDeserialize: true));
                    output.Add(asDecimal);
                    break;
                case JsonTokenType.PropertyName:
                case JsonTokenType.EndObject:
                default:
                    return Throw.Ex<List<object>>(new ConverterException("Operation should be impossible.", typeof(RumbleJson), onDeserialize: true));
            }
        return output;
    }
    #endregion READ

    #region WRITE

    public override void Write(Utf8JsonWriter writer, RumbleJson value, JsonSerializerOptions options) 
        => WriteJsonValue(ref writer, ref options, null, value);
    
    private static void WriteJsonValue(ref Utf8JsonWriter writer, ref JsonSerializerOptions options, string key, object value)
    {
        if (!string.IsNullOrWhiteSpace(key))
            writer.WritePropertyName(key);
        
        switch (value)
        {
            case bool asBool:
                writer.WriteBooleanValue(asBool);
                break;
            case Enum asEnum:
                writer.WriteStringValue(asEnum.ToString());
                break;
            case string asString:
                writer.WriteStringValue(asString);
                break;
            case short asShort:
                writer.WriteNumberValue(asShort);
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
            case double asDouble:
                writer.WriteNumberValue(asDouble);
                break;
            case decimal asDecimal:
                writer.WriteNumberValue(asDecimal);
                break;
            case null:
                writer.WriteNullValue();
                break;
            case RumbleJson asGeneric:
                writer.WriteStartObject();
                foreach ((string _key, object _value) in asGeneric)
                    WriteJsonValue(ref writer, ref options, _key, _value);
                writer.WriteEndObject();
                break;
            case IEnumerable asEnumerable:
                writer.WriteStartArray();
                foreach (object _value in asEnumerable)
                    WriteJsonValue(ref writer, ref options, null, _value);
                writer.WriteEndArray();
                break;
            case PlatformDataModel asModel:
                writer.WriteRawValue(asModel.ToJson());
                break;
            case MongoException asMongoException:
                writer.WriteRawValue(asMongoException.ToJson(new JsonWriterSettings
                {
                    OutputMode = JsonOutputMode.CanonicalExtendedJson
                }));
                break;
            default:
                try
                {
                    writer.WriteRawValue(JsonSerializer.Serialize(value, options));
                }
                catch (Exception e)
                {
                    Utilities.Log.Send("Could not serialize unexpected data type properly.", data: new RumbleJson
                    {
                        { "information", "A custom data type was likely passed into a RumbleJson object and JSON may not have serialized as expected." },
                        { "dataType", value.GetType() },
                        { "message", e?.Message }
                    });
                    throw;
                }
                break;
        }
    }
#endregion WRITE
}