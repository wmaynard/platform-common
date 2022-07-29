using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rumble.Platform.Common.Utilities.Serializers;

public class JsonTypeConverter : JsonConverter<Type>
{
    public override Type Read(ref Utf8JsonReader rdr, Type type, JsonSerializerOptions options) => Type.GetType(rdr.GetString());

    public override void Write(Utf8JsonWriter writer, Type type, JsonSerializerOptions options) => writer.WriteStringValue(type.AssemblyQualifiedName);
}