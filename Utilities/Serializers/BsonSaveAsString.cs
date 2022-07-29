using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Serializers;

namespace Rumble.Platform.Common.Utilities.Serializers;

public class BsonSaveAsString : BsonSerializerAttribute
{
    public BsonSaveAsString() : base(typeof(Serializer)) { }

    private class Serializer : SerializerBase<string>
    {
        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, string value) =>
            context.Writer.WriteString(value);

        public override string Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args) => context.Reader.CurrentBsonType switch
        {
            BsonType.Int32 => context.Reader.ReadInt32().ToString(),
            BsonType.Int64 => context.Reader.ReadInt64().ToString(),
            BsonType.Boolean => context.Reader.ReadBoolean().ToString(),
            BsonType.Decimal128 => context.Reader.ReadDecimal128().ToString(),
            BsonType.Null => null,
            BsonType.String => context.Reader.ReadString(),
            BsonType.DateTime => context.Reader.ReadDateTime().ToString(),
            BsonType.Double => context.Reader.ReadDouble().ToString(),
            BsonType.Timestamp => context.Reader.ReadTimestamp().ToString(),
            _ => throw new NotImplementedException()
        };
    }
}