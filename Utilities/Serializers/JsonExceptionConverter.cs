using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson.IO;

namespace Rumble.Platform.Common.Utilities.Serializers;

public class JsonExceptionConverter : JsonConverter<Exception>
{
    public override Exception Read(ref Utf8JsonReader rdr, Type type, JsonSerializerOptions options) => null;

    public override void Write(Utf8JsonWriter writer, Exception ex, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("Message", ex.Message);
        writer.WriteString("StackTrace", ex.StackTrace);
        writer.WriteString("Type", ex.GetType().Name);
        writer.WriteEndObject();
    }
}