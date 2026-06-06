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
    public async Task<string> PublishAsync(
        string urn,
        IReadOnlyDictionary<string, object?> data,
        string queue,
        string? traceId = null,
        CancellationToken cancellationToken = default)
    {
        var target = string.IsNullOrWhiteSpace(queue) ? _defaultQueue : queue;
        var envelope = EnvelopeCodec.Make(urn, data, target, traceId);

        var endpoint = await _sendEndpointProvider
            .GetSendEndpoint(new Uri($"queue:{target}"))
            .ConfigureAwait(false);
        await endpoint.Send(envelope, cancellationToken).ConfigureAwait(false);

        return envelope.Meta!.Id!;
    }
}
