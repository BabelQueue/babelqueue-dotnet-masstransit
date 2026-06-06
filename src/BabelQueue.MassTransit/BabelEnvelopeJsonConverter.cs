using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BabelQueue.MassTransit;

/// <summary>
/// A <see cref="System.Text.Json"/> converter that reads and writes the canonical
/// BabelQueue envelope via the core <see cref="EnvelopeCodec"/> — so any STJ-based
/// serializer (including MassTransit's raw JSON serializer) emits and parses the
/// envelope <b>byte-for-byte identically</b> to the PHP/Laravel, Python, Go, Node
/// and Java SDKs.
/// </summary>
public sealed class BabelEnvelopeJsonConverter : JsonConverter<Envelope>
{
    /// <inheritdoc />
    public override Envelope Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return EnvelopeCodec.Decode(document.RootElement.GetRawText());
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Envelope value, JsonSerializerOptions options)
    {
        // Write the core's canonical encoding verbatim (no re-escaping).
        writer.WriteRawValue(EnvelopeCodec.Encode(value));
    }
}
