using System;
using System.Text.Json.Serialization;

namespace Lokad.ContentAddr;

/// <summary> Passthrough converter, only write a json string </summary>
/// <remarks>
///     This is necessary because System.Text.Json does not honor the existence
///     of a TypeConverter to string.
/// </remarks>
public class HashJsonConverter : JsonConverter<Hash>
{
    public override Hash Read(
        ref System.Text.Json.Utf8JsonReader reader,
        Type typeToConvert,
        System.Text.Json.JsonSerializerOptions options)
    =>
        new(reader.GetString() ?? throw new ArgumentException("Missing string", nameof(reader)));

    public override void Write(System.Text.Json.Utf8JsonWriter writer, Hash value, System.Text.Json.JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}