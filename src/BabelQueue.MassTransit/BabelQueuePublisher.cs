using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using global::MassTransit;

namespace BabelQueue.MassTransit;

/// <summary>
/// Ergonomic producer over MassTransit's <see cref="ISendEndpointProvider"/>. It
/// builds the canonical envelope with the core codec and sends it to a queue; with
/// the BabelQueue serializer configured (see
/// <see cref="BabelQueueMassTransitExtensions.UseBabelQueueEnvelopes"/>), the wire
/// body is the canonical envelope that any BabelQueue SDK can read.
/// </summary>
public sealed class BabelQueuePublisher
{
    private readonly ISendEndpointProvider _sendEndpointProvider;
    private readonly string _defaultQueue;

    public BabelQueuePublisher(ISendEndpointProvider sendEndpointProvider, string defaultQueue = "default")
    {
        _sendEndpointProvider = sendEndpointProvider;
        _defaultQueue = string.IsNullOrWhiteSpace(defaultQueue) ? "default" : defaultQueue;
    }

    /// <summary>Publish a <c>(urn, data)</c> message to the default queue. Returns <c>meta.id</c>.</summary>
    public Task<string> PublishAsync(
        string urn,
        IReadOnlyDictionary<string, object?> data,
        CancellationToken cancellationToken = default)
        => PublishAsync(urn, data, _defaultQueue, null, cancellationToken);

    /// <summary>Publish a <c>(urn, data)</c> message to <paramref name="queue"/>, optionally continuing a trace.</summary>
    public Task<string> PublishAsync(
        string urn,
        IReadOnlyDictionary<string, object?> data,
        string queue,
        string? traceId = null,
        CancellationToken cancellationToken = default)
        => PublishWithHeadersAsync(urn, data, headers: null, queue, traceId, cancellationToken);

    /// <summary>
    /// The header-aware (ADR-0028) counterpart of
    /// <see cref="PublishAsync(string, IReadOnlyDictionary{string, object?}, string, string?, CancellationToken)"/>:
    /// it sends the canonical envelope and writes the out-of-band <paramref name="headers"/> (e.g. a
    /// W3C <c>traceparent</c> from <c>Telemetry.PublishAsync(..., headers, ...)</c>) onto the
    /// message's <see cref="SendContext.Headers"/> — MassTransit's native metadata channel — beside
    /// the frozen envelope (GR-1), never inside it. A consumer reads them back from
    /// <c>ConsumeContext.Headers</c> via <see cref="MassTransitHeaders.Extract"/>. A <c>null</c>/empty
    /// header map is identical to
    /// <see cref="PublishAsync(string, IReadOnlyDictionary{string, object?}, string, string?, CancellationToken)"/>.
    /// </summary>
    /// <param name="urn">The message URN to publish.</param>
    /// <param name="data">The message payload.</param>
    /// <param name="headers">The out-of-band transport headers to ride beside the envelope.</param>
    /// <param name="queue">The destination queue (falls back to the default when blank).</param>
    /// <param name="traceId">An existing trace id to continue, or <c>null</c> to mint one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<string> PublishWithHeadersAsync(
        string urn,
        IReadOnlyDictionary<string, object?> data,
        IReadOnlyDictionary<string, string>? headers,
        string? queue = null,
        string? traceId = null,
        CancellationToken cancellationToken = default)
    {
        var target = string.IsNullOrWhiteSpace(queue) ? _defaultQueue : queue;
        var envelope = EnvelopeCodec.Make(urn, data, target, traceId);

        var endpoint = await _sendEndpointProvider
            .GetSendEndpoint(new Uri($"queue:{target}"))
            .ConfigureAwait(false);

        var pipe = Pipe.Execute<SendContext<Envelope>>(context => MassTransitHeaders.Apply(context, headers));
        await endpoint.Send(envelope, pipe, cancellationToken).ConfigureAwait(false);

        return envelope.Meta!.Id!;
    }
}
