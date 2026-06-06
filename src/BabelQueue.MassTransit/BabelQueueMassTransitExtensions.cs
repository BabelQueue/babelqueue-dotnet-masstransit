using System.Linq;
using System.Text.Json;
using global::MassTransit;
using global::MassTransit.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace BabelQueue.MassTransit;

/// <summary>Configuration helpers wiring BabelQueue into MassTransit.</summary>
public static class BabelQueueMassTransitExtensions
{
    /// <summary>
    /// Register the BabelQueue envelope converter on MassTransit's System.Text.Json
    /// options and switch the bus to raw JSON, so messages on the wire are the
    /// canonical BabelQueue envelope (readable by every other SDK). Call this inside
    /// your bus configuration (e.g. <c>UsingRabbitMq((ctx, cfg) =&gt; cfg.UseBabelQueueEnvelopes())</c>).
    /// </summary>
    public static void UseBabelQueueEnvelopes(this IBusFactoryConfigurator configurator)
    {
        AddConverter(SystemTextJsonMessageSerializer.Options);
        configurator.UseRawJsonSerializer();
    }

    /// <summary>
    /// Add the BabelQueue envelope converter to any <see cref="JsonSerializerOptions"/>
    /// (idempotent). Useful for standalone System.Text.Json usage.
    /// </summary>
    public static JsonSerializerOptions AddBabelQueueEnvelopes(this JsonSerializerOptions options)
    {
        AddConverter(options);
        return options;
    }

    /// <summary>
    /// Register <see cref="BabelQueuePublisher"/> in the container (it resolves
    /// MassTransit's <see cref="ISendEndpointProvider"/>).
    /// </summary>
    public static IServiceCollection AddBabelQueuePublisher(
        this IServiceCollection services,
        string defaultQueue = "default")
    {
        services.AddScoped(provider =>
            new BabelQueuePublisher(provider.GetRequiredService<ISendEndpointProvider>(), defaultQueue));
        return services;
    }

    private static void AddConverter(JsonSerializerOptions options)
    {
        if (!options.Converters.OfType<BabelEnvelopeJsonConverter>().Any())
        {
            options.Converters.Add(new BabelEnvelopeJsonConverter());
        }
    }
}
