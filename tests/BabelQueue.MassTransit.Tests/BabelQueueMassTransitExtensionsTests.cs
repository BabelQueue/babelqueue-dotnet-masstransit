using System.Linq;
using System.Text.Json;
using BabelQueue.MassTransit;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace BabelQueue.MassTransit.Tests;

public class BabelQueueMassTransitExtensionsTests
{
    [Fact]
    public void AddBabelQueueEnvelopes_AddsConverterIdempotently()
    {
        var options = new JsonSerializerOptions();

        options.AddBabelQueueEnvelopes();
        options.AddBabelQueueEnvelopes(); // a second call must not duplicate it

        Assert.Single(options.Converters.OfType<BabelEnvelopeJsonConverter>());
    }

    [Fact]
    public void AddBabelQueuePublisher_RegistersAResolvablePublisher()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<ISendEndpointProvider>());

        services.AddBabelQueuePublisher("orders");

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<BabelQueuePublisher>());
    }

    [Fact]
    public void UseBabelQueueEnvelopes_ConfiguresAnInMemoryBus()
    {
        // A real (broker-free) in-memory bus configurator exercises the bus-config
        // path: AddConverter + UseRawJsonSerializer.
        var bus = Bus.Factory.CreateUsingInMemory(cfg => cfg.UseBabelQueueEnvelopes());

        Assert.NotNull(bus);
    }
}
