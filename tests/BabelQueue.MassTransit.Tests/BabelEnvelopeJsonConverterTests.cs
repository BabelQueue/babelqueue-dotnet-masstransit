using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BabelQueue;
using BabelQueue.MassTransit;
using Xunit;

namespace BabelQueue.MassTransit.Tests;

public class BabelEnvelopeJsonConverterTests
{
    private static JsonSerializerOptions Options() => new JsonSerializerOptions().AddBabelQueueEnvelopes();

    [Fact]
    public void Serialize_ProducesByteForByteCanonicalEnvelope()
    {
        var env = EnvelopeCodec.Make(
            "urn:babel:orders:created",
            new Dictionary<string, object?> { ["order_id"] = 1042L },
            "orders",
            "t-1");

        var json = JsonSerializer.Serialize(env, Options());

        Assert.Equal(EnvelopeCodec.Encode(env), json);
    }

    [Fact]
    public void Deserialize_ParsesACanonicalEnvelope()
    {
        var env = EnvelopeCodec.Make(
            "urn:babel:orders:created",
            new Dictionary<string, object?> { ["order_id"] = 7L },
            "orders",
            null);
        var json = EnvelopeCodec.Encode(env);

        var got = JsonSerializer.Deserialize<Envelope>(json, Options())!;

        Assert.True(EnvelopeCodec.Accepts(got));
        Assert.Equal("urn:babel:orders:created", EnvelopeCodec.Urn(got));
        Assert.Equal(7L, got.Data!["order_id"]);
        Assert.Equal(env.TraceId, got.TraceId);
    }

    [Fact]
    public void AddBabelQueueEnvelopes_IsIdempotent()
    {
        var options = new JsonSerializerOptions();
        options.AddBabelQueueEnvelopes().AddBabelQueueEnvelopes();

        Assert.Single(options.Converters.OfType<BabelEnvelopeJsonConverter>());
    }
}
