using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BabelQueue;
using BabelQueue.MassTransit;
using MassTransit;
using Moq;
using Xunit;

namespace BabelQueue.MassTransit.Tests;

public class BabelQueuePublisherTests
{
    [Fact]
    public async Task PublishAsync_BuildsEnvelopeAndSendsToTheQueue()
    {
        Envelope? sent = null;
        var endpoint = new Mock<ISendEndpoint>();
        endpoint
            .Setup(e => e.Send(It.IsAny<Envelope>(), It.IsAny<CancellationToken>()))
            .Callback<Envelope, CancellationToken>((message, _) => sent = message)
            .Returns(Task.CompletedTask);

        Uri? endpointUri = null;
        var provider = new Mock<ISendEndpointProvider>();
        provider
            .Setup(p => p.GetSendEndpoint(It.IsAny<Uri>()))
            .Callback<Uri>(uri => endpointUri = uri)
            .ReturnsAsync(endpoint.Object);

        var publisher = new BabelQueuePublisher(provider.Object, "default");

        var id = await publisher.PublishAsync(
            "urn:babel:orders:created",
            new Dictionary<string, object?> { ["order_id"] = 1042L },
            "orders");

        Assert.NotNull(sent);
        Assert.Equal("urn:babel:orders:created", sent!.Job);
        Assert.Equal("orders", sent.Meta!.Queue);
        Assert.Equal("dotnet", sent.Meta.Lang);
        Assert.Equal(1042L, sent.Data!["order_id"]);
        Assert.Equal(id, sent.Meta.Id);
        Assert.Equal("queue:orders", endpointUri!.ToString());
    }

    [Fact]
    public async Task PublishAsync_FallsBackToTheDefaultQueue()
    {
        var endpoint = new Mock<ISendEndpoint>();
        endpoint
            .Setup(e => e.Send(It.IsAny<Envelope>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Uri? endpointUri = null;
        var provider = new Mock<ISendEndpointProvider>();
        provider
            .Setup(p => p.GetSendEndpoint(It.IsAny<Uri>()))
            .Callback<Uri>(uri => endpointUri = uri)
            .ReturnsAsync(endpoint.Object);

        var publisher = new BabelQueuePublisher(provider.Object, "events");
        await publisher.PublishAsync("urn:babel:orders:created", new Dictionary<string, object?>());

        Assert.Equal("queue:events", endpointUri!.ToString());
    }
}
