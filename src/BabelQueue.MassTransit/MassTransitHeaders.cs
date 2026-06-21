using System;
using System.Collections.Generic;
using System.Globalization;
using global::MassTransit;

namespace BabelQueue.MassTransit;

/// <summary>
/// Out-of-band transport-header carrier for MassTransit (ADR-0028) — the MassTransit counterpart of
/// the SQS <c>MessageAttributes</c> / Redis frame seam and the .NET core's
/// <see cref="BabelQueue.Tracing.Traceparent"/>.
/// </summary>
/// <remarks>
/// MassTransit has a native per-message metadata channel, so a W3C <c>traceparent</c> (and
/// <c>tracestate</c>) for cross-hop span linkage rides on <see cref="SendContext.Headers"/> on the
/// produce side and is read back from <see cref="MessageContext.Headers"/>
/// (<see cref="ConsumeContext"/>) on the consume side — <b>beside</b> the canonical wire envelope,
/// never inside it (GR-1; the envelope and its <c>schema_version</c> are untouched). With no
/// <c>traceparent</c> header the core falls back to the v0.1 <c>trace_id</c> mapping (no regression).
/// </remarks>
public static class MassTransitHeaders
{
    /// <summary>
    /// Writes the out-of-band string <paramref name="headers"/> onto the message's
    /// <see cref="SendContext.Headers"/> (e.g. a W3C <c>traceparent</c> from
    /// <c>Telemetry.PublishAsync(..., headers, ...)</c>). Empty keys/values are skipped. A
    /// <c>null</c>/empty map writes nothing, so a header-less send is identical to before.
    /// </summary>
    public static void Apply(SendContext context, IReadOnlyDictionary<string, string>? headers)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (headers is null || headers.Count == 0)
        {
            return;
        }

        foreach (var (key, value) in headers)
        {
            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
            {
                context.Headers.Set(key, value);
            }
        }
    }

    /// <summary>
    /// Maps a delivered message's <see cref="MessageContext.Headers"/> (e.g.
    /// <c>consumeContext.Headers</c>) to a flat <see cref="Dictionary{TKey,TValue}"/> a handler hands
    /// to <c>Telemetry.Wrap(handler, headers)</c>. Each header value is rendered with the invariant
    /// culture; empty keys/values are skipped. A <c>null</c> input or a header-less delivery yields
    /// an empty map, so the consumer falls back to the v0.1 <c>trace_id</c> mapping.
    /// </summary>
    public static Dictionary<string, string> Extract(Headers? headers)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (headers is null)
        {
            return result;
        }

        foreach (var (key, value) in headers.GetAll())
        {
            if (string.IsNullOrEmpty(key) || value is null)
            {
                continue;
            }

            var text = value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(text))
            {
                result[key] = text;
            }
        }

        return result;
    }
}
