using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BabelQueue;
using BabelQueue.MassTransit;
using Xunit;

namespace BabelQueue.MassTransit.Tests;

/// <summary>
/// Runs the shared cross-SDK conformance suite (vendored under <c>conformance/</c>,
/// copied to the test output) through the MassTransit adapter's System.Text.Json
/// path — <see cref="BabelEnvelopeJsonConverter"/> registered via
/// <c>AddBabelQueueEnvelopes()</c> — the same fixtures every BabelQueue SDK must
/// satisfy. Per-message fields (meta.id, trace_id, meta.created_at) are
/// intrinsically unique and are checked for presence only.
/// </summary>
public class BabelQueueConformanceTests
{
    private static string SuitePath(string relative) =>
        Path.Combine(AppContext.BaseDirectory, "conformance", relative);

    private static JsonSerializerOptions Options() => new JsonSerializerOptions().AddBabelQueueEnvelopes();

    public static IEnumerable<object[]> Cases()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SuitePath("manifest.json")));
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            yield return new object[] { c.GetProperty("name").GetString()! };
        }
    }

    [Fact]
    public void ManifestMatchesCoreSchemaVersion()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SuitePath("manifest.json")));
        Assert.Equal(EnvelopeCodec.SchemaVersion, doc.RootElement.GetProperty("schema_version").GetInt32());
        Assert.NotEmpty(doc.RootElement.GetProperty("cases").EnumerateArray());
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ConformanceThroughTheJsonConverter(string caseName)
    {
        var c = FindCase(caseName);
        var body = File.ReadAllText(SuitePath(c.GetProperty("file").GetString()!));

        // Consume path: the STJ converter parses the fixture into the core Envelope.
        var env = JsonSerializer.Deserialize<Envelope>(body, Options())!;

        if (!c.GetProperty("valid").GetBoolean())
        {
            var reason = c.TryGetProperty("reason", out var r) ? r.GetString() : "";
            Assert.False(EnvelopeCodec.Accepts(env), $"invalid fixture must be rejected ({reason})");
            return;
        }

        Assert.True(EnvelopeCodec.Accepts(env), "valid fixture must be accepted");
        var expect = c.GetProperty("expect");
        AssertExpectations(env, expect);

        // Produce path: the converter writes the core's canonical encoding verbatim,
        // and what it writes parses back to the same expectations.
        var json = JsonSerializer.Serialize(env, Options());
        Assert.Equal(EnvelopeCodec.Encode(env), json);

        var reDecoded = JsonSerializer.Deserialize<Envelope>(json, Options())!;
        Assert.True(EnvelopeCodec.Accepts(reDecoded), "re-serialized envelope must be accepted");
        AssertExpectations(reDecoded, expect);
        Assert.Equal(env.TraceId, reDecoded.TraceId);
        Assert.Equal(env.Meta!.Id, reDecoded.Meta!.Id);
    }

    private static List<JsonElement> SectionCases(string section)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SuitePath("manifest.json")));
        Assert.True(doc.RootElement.TryGetProperty(section, out var s), $"manifest has no '{section}' section");
        var cases = s.GetProperty("cases").EnumerateArray().Select(c => c.Clone()).ToList();
        Assert.NotEmpty(cases);
        return cases;
    }

    private static string Fixture(JsonElement c) =>
        File.ReadAllText(SuitePath(c.GetProperty("file").GetString()!));

    /// <summary>
    /// <c>roundtrip</c>: deserialize -> attempts+1 -> serialize through the STJ converter;
    /// unknown keys must survive unchanged (forward-compatible re-emit).
    /// </summary>
    [Fact]
    public void RoundtripPreservesUnknownKeysThroughTheJsonConverter()
    {
        foreach (var c in SectionCases("roundtrip"))
        {
            var name = c.GetProperty("name").GetString();
            var env = JsonSerializer.Deserialize<Envelope>(Fixture(c), Options())!;
            Assert.True(EnvelopeCodec.Accepts(env), $"{name}: fixture must be accepted");

            var json = JsonSerializer.Serialize(env with { Attempts = env.Attempts + 1 }, Options());
            using var output = JsonDocument.Parse(json);

            Assert.Equal(c.GetProperty("expect_attempts").GetInt32(), output.RootElement.GetProperty("attempts").GetInt32());
            foreach (var expectation in c.GetProperty("expect_preserved").EnumerateObject())
            {
                var actual = Resolve(output.RootElement, expectation.Name);
                Assert.True(actual.HasValue, $"{name}: {expectation.Name} was dropped on re-emit");
                Assert.True(
                    DeepEquals(expectation.Value, actual!.Value),
                    $"{name}: {expectation.Name} expected {expectation.Value.GetRawText()} but was {actual.Value.GetRawText()}");
            }
        }
    }

    /// <summary>
    /// <c>forbidden_keys</c>: the converter must accept the message (K-15 warns, never
    /// rejects) and must never re-emit the forbidden key. The warning hook belongs to the
    /// core codec (asserted by the core suite); the converter exposes none.
    /// </summary>
    [Fact]
    public void ForbiddenKeysAreNeverReEmittedThroughTheJsonConverter()
    {
        foreach (var c in SectionCases("forbidden_keys"))
        {
            var name = c.GetProperty("name").GetString();
            Assert.Equal("warn", c.GetProperty("expect").GetString());
            var env = JsonSerializer.Deserialize<Envelope>(Fixture(c), Options())!;
            Assert.True(EnvelopeCodec.Accepts(env), $"{name}: forbidden key must not reject the message");

            using var output = JsonDocument.Parse(JsonSerializer.Serialize(env, Options()));
            foreach (var pointer in c.GetProperty("expect_absent_after_reencode").EnumerateArray())
            {
                Assert.False(
                    Resolve(output.RootElement, pointer.GetString()!).HasValue,
                    $"{name}: {pointer.GetString()} must not be re-emitted");
            }
        }
    }

    /// <summary>
    /// <c>data_shape</c>: <c>encode</c> cases build an envelope and serialize it through the
    /// converter (<c>{}</c> stays an object, never <c>[]</c>); <c>decode</c> cases must match
    /// the manifest's <c>valid</c> verdict.
    /// </summary>
    [Fact]
    public void DataShapeThroughTheJsonConverter()
    {
        var modes = new HashSet<string>();
        foreach (var c in SectionCases("data_shape"))
        {
            var name = c.GetProperty("name").GetString();
            var mode = c.GetProperty("mode").GetString()!;
            modes.Add(mode);

            if (mode == "encode")
            {
                var data = ToValue(c.GetProperty("data")) as IReadOnlyDictionary<string, object?>;
                var env = EnvelopeCodec.Make(c.GetProperty("urn").GetString()!, data, c.GetProperty("queue").GetString()!);
                using var output = JsonDocument.Parse(JsonSerializer.Serialize(env, Options()));
                var dataJson = JsonSerializer.Serialize(output.RootElement.GetProperty("data"));
                Assert.Equal(c.GetProperty("expect_encoded_data_json").GetString(), dataJson);
            }
            else
            {
                Assert.Equal("decode", mode);
                var env = JsonSerializer.Deserialize<Envelope>(Fixture(c), Options())!;
                Assert.True(
                    c.GetProperty("valid").GetBoolean() == EnvelopeCodec.Accepts(env),
                    $"{name}: expected valid={c.GetProperty("valid").GetBoolean()}");
            }
        }
        Assert.Contains("encode", modes);
        Assert.Contains("decode", modes);
    }

    /// <summary>RFC 6901 pointer resolution; null when the path does not exist.</summary>
    private static JsonElement? Resolve(JsonElement root, string pointer)
    {
        var current = root;
        if (pointer.Length == 0)
        {
            return current;
        }
        foreach (var rawToken in pointer[1..].Split('/'))
        {
            var token = rawToken.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            if (current.ValueKind == JsonValueKind.Object)
            {
                if (!current.TryGetProperty(token, out current))
                {
                    return null;
                }
            }
            else if (current.ValueKind == JsonValueKind.Array
                && int.TryParse(token, out var index) && index >= 0 && index < current.GetArrayLength())
            {
                current = current[index];
            }
            else
            {
                return null;
            }
        }
        return current;
    }

    /// <summary>Type-strict deep equality: 1 != 1.0, {} != [], object key order ignored.</summary>
    private static bool DeepEquals(JsonElement expected, JsonElement actual)
    {
        if (expected.ValueKind != actual.ValueKind)
        {
            return false;
        }
        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                var props = expected.EnumerateObject().ToList();
                return props.Count == actual.EnumerateObject().Count()
                    && props.All(p => actual.TryGetProperty(p.Name, out var other) && DeepEquals(p.Value, other));
            case JsonValueKind.Array:
                var left = expected.EnumerateArray().ToList();
                var right = actual.EnumerateArray().ToList();
                return left.Count == right.Count && left.Zip(right).All(pair => DeepEquals(pair.First, pair.Second));
            case JsonValueKind.Number:
                return expected.GetRawText() == actual.GetRawText();
            case JsonValueKind.String:
                return expected.GetString() == actual.GetString();
            default:
                return true;
        }
    }

    private static void AssertExpectations(Envelope env, JsonElement expect)
    {
        Assert.Equal(expect.GetProperty("urn").GetString(), EnvelopeCodec.Urn(env));
        Assert.Equal(expect.GetProperty("attempts").GetInt32(), env.Attempts);
        Assert.Equal(expect.GetProperty("lang").GetString(), env.Meta!.Lang);
        Assert.Equal(expect.GetProperty("schema_version").GetInt32(), env.Meta.SchemaVersion);

        if (expect.TryGetProperty("data", out var dataElement))
        {
            Assert.True(JsonValueEquals(env.Data, ToValue(dataElement)), "data mismatch");
        }

        Assert.False(string.IsNullOrEmpty(env.TraceId));
        Assert.False(string.IsNullOrEmpty(env.Meta.Id));
        Assert.NotEqual(0, env.Meta.CreatedAt);

        if (expect.TryGetProperty("dead_letter", out var dlElement))
        {
            Assert.NotNull(env.DeadLetter);
            if (dlElement.TryGetProperty("reason", out var reason))
            {
                Assert.Equal(reason.GetString(), env.DeadLetter!.Reason);
            }
            if (dlElement.TryGetProperty("original_queue", out var originalQueue))
            {
                Assert.Equal(originalQueue.GetString(), env.DeadLetter!.OriginalQueue);
            }
        }
    }

    private static JsonElement FindCase(string name)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SuitePath("manifest.json")));
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (c.GetProperty("name").GetString() == name)
            {
                return c.Clone();
            }
        }
        throw new InvalidOperationException($"Conformance case not found: {name}");
    }

    private static object? ToValue(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => e.EnumerateObject().ToDictionary(p => p.Name, p => ToValue(p.Value)),
        JsonValueKind.Array => e.EnumerateArray().Select(ToValue).ToList(),
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.TryGetInt64(out var l) ? (object)l : e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    private static bool JsonValueEquals(object? a, object? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }
        if (a is IReadOnlyDictionary<string, object?> da && b is IReadOnlyDictionary<string, object?> db)
        {
            if (da.Count != db.Count)
            {
                return false;
            }
            foreach (var kv in da)
            {
                if (!db.TryGetValue(kv.Key, out var bv) || !JsonValueEquals(kv.Value, bv))
                {
                    return false;
                }
            }
            return true;
        }
        if (a is System.Collections.IList la && b is System.Collections.IList lb)
        {
            if (la.Count != lb.Count)
            {
                return false;
            }
            for (var i = 0; i < la.Count; i++)
            {
                if (!JsonValueEquals(la[i], lb[i]))
                {
                    return false;
                }
            }
            return true;
        }
        return a.Equals(b);
    }
}
