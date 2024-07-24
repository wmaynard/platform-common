using System;
using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using Rumble.Platform.Common.Utilities.JsonTools.Exceptions;
using Rumble.Platform.Common.Utilities.JsonTools.Utilities;

namespace Rumble.Platform.Common.Utilities.JsonTools.Serializers;

public class BsonGenericConverter : SerializerBase<RumbleJson>
{
    #region READ
    public override RumbleJson Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        BsonBinaryReader reader = null;
        try
        {
            reader = (BsonBinaryReader)context.Reader;

            switch (reader.CurrentBsonType)
            {
                case BsonType.Document:
                    BsonDocument bson = BsonSerializer.Deserialize<BsonDocument>(reader);

                    return ParseDocument(bson);
                case BsonType.Null:
                case BsonType.Undefined:
                    reader.SkipValue();  // IMPORTANT: Without the SkipValue(), the reader will be left in a bad state
                    return null;         // and corrupt the next read.
                case BsonType.Array:
                    Utilities.Log.Send("BsonType of Array is likely not supported; attempting a BsonDocument read anyway.", new()
                    {
                        { "state", reader.State },
                        { "currentBsonType", reader.CurrentBsonType }
                    });
                    goto case BsonType.Document;
                case BsonType.Boolean:
                case BsonType.Binary:
                case BsonType.DateTime:
                case BsonType.Decimal128:
                case BsonType.Double:
                case BsonType.EndOfDocument:
                case BsonType.Int32:
                case BsonType.Int64:
                case BsonType.JavaScript:
                case BsonType.JavaScriptWithScope:
                case BsonType.MaxKey:
                case BsonType.MinKey:
                case BsonType.ObjectId:
                case BsonType.RegularExpression:
                case BsonType.String:
                case BsonType.Symbol:
                case BsonType.Timestamp:
                    Utilities.Log.Send("Unexpected BsonType in RumbleJson serialization; attempting a BsonDocument read.", new()
                    {
                        { "state", reader.State },
                        { "currentBsonType", reader.CurrentBsonType }
                    });
                    goto case BsonType.Document;
                default:
                    return Throw.Ex<RumbleJson>(new ConverterException($"Unable to deserialize from BsonBinaryReader: {reader.State}.", typeof(RumbleJson), onDeserialize: true));
            }
        }
        catch (Exception e)
        {
            return Throw.Ex<RumbleJson>(new ConverterException($"Unable to deserialize from BsonBinaryReader: {reader?.State}.", typeof(RumbleJson), onDeserialize: true));
        }
    }

    private RumbleJson ParseDocument(BsonDocument bson)
    {
        RumbleJson output = new();

        foreach (BsonElement element in bson)
            output[element.Name] = ParseValue(element.Value);
        return output;
    }

    private object[] ParseArray(BsonArray array) => array
        .Select(ParseValue)
        .ToArray();

    private object ParseValue(BsonValue value) => value.BsonType switch
    {
        BsonType.EndOfDocument => throw new NotImplementedException(),
        BsonType.Double => value.AsDouble,
        BsonType.String => value.AsString,
        BsonType.Document => ParseDocument(value.AsBsonDocument),
        BsonType.Array => ParseArray(value.AsBsonArray),
        BsonType.Binary => throw new NotImplementedException(),
        BsonType.Undefined => null,
        BsonType.ObjectId => value.AsString,
        BsonType.Boolean => value.AsBoolean,
        BsonType.DateTime => value.AsBsonDateTime.ToUniversalTime(),
        BsonType.Null => null,
        BsonType.RegularExpression => value.AsString,
        BsonType.JavaScript => value.AsString,
        BsonType.Symbol => throw new NotImplementedException(),
        BsonType.JavaScriptWithScope => throw new NotImplementedException(),
        BsonType.Int32 => value.AsInt32,
        BsonType.Timestamp => value.AsBsonTimestamp.Value,
        BsonType.Int64 => value.AsInt64,
        BsonType.Decimal128 => value.AsDecimal,
        BsonType.MinKey => throw new NotImplementedException(),
        BsonType.MaxKey => throw new NotImplementedException(),
        _ => throw new ArgumentOutOfRangeException()
    };
    #endregion READ

    #region WRITE

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, RumbleJson value)
    {
        IBsonWriter writer = context.Writer;
        
        WriteBson(ref writer, value);
    }

    /// <summary>
    /// Writes a RumbleJson object to BSON for MongoDB.
    /// </summary>
    private void WriteBson(ref IBsonWriter writer, RumbleJson json)
    {
        if (json == null)
        {
            writer.WriteNull();
            return;
        }

        writer.WriteStartDocument();
        foreach ((string key, object value) in json)
            WriteValue(ref writer, key, value);
        writer.WriteEndDocument();
    }

    private void WriteValue(ref IBsonWriter writer, string key, object value)
    {
        if (!string.IsNullOrWhiteSpace(key))
            writer.WriteName(key);
        
        switch (value)
        {
            case bool asBool:
                writer.WriteBoolean(asBool);
                break;
            case string asString:
                writer.WriteString(asString);
                break;
            case int asInt:
                writer.WriteInt32(asInt);
                break;
            case long asLong:
                writer.WriteInt64(asLong);
                break;
            case decimal asDecimal:
                writer.WriteDecimal128(asDecimal);
                break;
            case IEnumerable<object> asEnumerable:
                writer.WriteStartArray();
                foreach (object obj in asEnumerable)
                    WriteValue(ref writer, null, obj);
                writer.WriteEndArray();
                break;
            case DateTime asDateTime:
                writer.WriteDateTime((long)asDateTime.Subtract(DateTime.UnixEpoch).TotalMilliseconds);
                break;
            case RumbleJson asJson:
                WriteBson(ref writer, asJson);
                break;
            case null:
                writer.WriteNull();
                break;
            default:
                Throw.Ex<object>(new ConverterException($"Unexpected datatype.", value.GetType()));
                break;
        }
    }
    #endregion WRITE
}