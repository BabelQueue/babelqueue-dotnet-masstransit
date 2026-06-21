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

/// <summary>
/// ADR-0028 out-of-band header carrier for MassTransit: a W3C <c>traceparent</c> rides on
/// <see cref="SendContext.Headers"/> (produce) and is read back from <c>ConsumeContext.Headers</c>
/// (consume) — beside the canonical envelope (GR-1; the body and its <c>schema_version</c> are
/// untouched). The end-to-end test runs on a broker-free in-memory bus.
/// </summary>
public class MassTransitHeadersTests
{
    private const string Traceparent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

    [Fact]
    public async Task PublishWithHeaders_WritesTraceparentOntoTheSendContextHeaders()
    {
        IPipe<SendContext<Envelope>>? capturedPipe = null;
        var endpoint = new Mock<ISendEndpoint>();
        endpoint
            .Setup(e => e.Send(It.IsAny<Envelope>(), It.IsAny<IPipe<SendContext<Envelope>>>(), It.IsAny<CancellationToken>()))
            .Callback<Envelope, IPipe<SendContext<Envelope>>, CancellationToken>((_, pipe, _) => capturedPipe = pipe)
            .Returns(Task.CompletedTask);

        var provider = new Mock<ISendEndpointProvider>();
        provider.Setup(p => p.GetSendEndpoint(It.IsAny<Uri>())).ReturnsAsync(endpoint.Object);

        await new BabelQueuePublisher(provider.Object, "orders").PublishWithHeadersAsync(
            "urn:babel:orders:created",
            new Dictionary<string, object?> { ["order_id"] = 1 },
            new Dictionary<string, string> { ["traceparent"] = Traceparent });

        // Drive the captured send pipe against a fake SendContext and assert the header was set.
        Assert.NotNull(capturedPipe);
        var headers = new Mock<SendHeaders>();
        var setHeaders = new Dictionary<string, string>(StringComparer.Ordinal);
        headers.Setup(h => h.Set(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((k, v) => setHeaders[k] = v);
        var context = new Mock<SendContext<Envelope>>();
        context.SetupGet(c => c.Headers).Returns(headers.Object);

        await capturedPipe!.Send(context.Object);

        Assert.Equal(Traceparent, setHeaders["traceparent"]);
    }

    [Fact]
    public void Apply_SkipsEmptyAndNull()
    {
        var setHeaders = new Dictionary<string, string>(StringComparer.Ordinal);
        var headers = new Mock<SendHeaders>();
        headers.Setup(h => h.Set(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((k, v) => setHeaders[k] = v);
        var context = new Mock<SendContext>();
        context.SetupGet(c => c.Headers).Returns(headers.Object);

        MassTransitHeaders.Apply(context.Object, new Dictionary<string, string> { [""] = "v", ["k"] = "", ["traceparent"] = Traceparent });
        MassTransitHeaders.Apply(context.Object, null);

        Assert.Single(setHeaders);
        Assert.Equal(Traceparent, setHeaders["traceparent"]);
    }

    [Fact]
    public void Extract_ReadsConsumeHeadersAndSkipsEmpty()
    {
        var headers = new Mock<Headers>();
        headers.Setup(h => h.GetAll()).Returns(new[]
        {
            new KeyValuePair<string, object>("traceparent", Traceparent),
            new KeyValuePair<string, object>("bq-job", "urn:babel:orders:created"),
            new KeyValuePair<string, object>("blank", ""),
            new KeyValuePair<string, object>("nullv", null!),
        });

        var result = MassTransitHeaders.Extract(headers.Object);

        Assert.Equal(Traceparent, result["traceparent"]);
        Assert.Equal("urn:babel:orders:created", result["bq-job"]);
        Assert.DoesNotContain("blank", result.Keys);
        Assert.DoesNotContain("nullv", result.Keys);
    }

    [Fact]
    public void Extract_OfNullYieldsEmptyMap() => Assert.Empty(MassTransitHeaders.Extract(null));

    [Fact]
    public async Task EndToEnd_TraceparentRidesTheBusHeaderBesideTheEnvelope()
    {
        // A real broker-free in-memory bus: produce with a traceparent header, consume, and assert
        // ConsumeContext.Headers carried it (beside the canonical envelope, which stays intact).
        var received = new TaskCompletionSource<ConsumeContext<Envelope>>(TaskCreationOptions.RunContinuationsAsynchronously);

        var bus = Bus.Factory.CreateUsingInMemory(cfg =>
        {
            cfg.UseBabelQueueEnvelopes();
            cfg.ReceiveEndpoint("orders", e => e.Handler<Envelope>(ctx =>
            {
                received.TrySetResult(ctx);
                return Task.CompletedTask;
            }));
        });

        await bus.StartAsync();
        try
        {
            var publisher = new BabelQueuePublisher(bus, "orders");
            var id = await publisher.PublishWithHeadersAsync(
                "urn:babel:orders:created",
                new Dictionary<string, object?> { ["order_id"] = 42L },
                new Dictionary<string, string> { ["traceparent"] = Traceparent });

            var ctx = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // The traceparent rode the bus header, beside the canonical envelope.
            var headers = MassTransitHeaders.Extract(ctx.Headers);
            Assert.Equal(Traceparent, headers["traceparent"]);

            // GR-1 / GR-4: the envelope is intact — same id, schema_version 1, trace_id preserved.
            Assert.Equal(id, ctx.Message.Meta!.Id);
            Assert.Equal(1, ctx.Message.Meta.SchemaVersion);
            Assert.Equal("urn:babel:orders:created", ctx.Message.Job);

            // The extracted header rebuilds a real remote parent for Telemetry.Wrap.
            var parent = BabelQueue.Tracing.Traceparent.RemoteParentFromHeaders(headers);
            Assert.NotNull(parent);
            Assert.Equal("0af7651916cd43dd8448eb211c80319c", parent!.Value.TraceId.ToHexString());
        }
        finally
        {
            await bus.StopAsync();
        }
    }
}
