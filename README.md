# BabelQueue for MassTransit

[![CI](https://github.com/BabelQueue/babelqueue-dotnet-masstransit/actions/workflows/ci.yml/badge.svg)](https://github.com/BabelQueue/babelqueue-dotnet-masstransit/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/BabelQueue.MassTransit.svg)](https://www.nuget.org/packages/BabelQueue.MassTransit)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

> **Polyglot Queues, Simplified.** A [MassTransit](https://masstransit.io) adapter
> that makes your MassTransit services produce and consume the canonical BabelQueue
> envelope — so they exchange messages with the Laravel, Symfony, Python, Go, Node
> and Java SDKs over one strict JSON format.

This is the **MassTransit adapter** on top of
[`BabelQueue.Core`](https://www.nuget.org/packages/BabelQueue.Core): a
`System.Text.Json` converter (byte-for-byte canonical envelopes), an ergonomic
publisher and configuration helpers. The full standard is documented at
**[babelqueue.com](https://babelqueue.com)**.

## Installation

```bash
dotnet add package BabelQueue.MassTransit
```

Targets **.NET 8**; requires MassTransit **8+**.

## How it works

MassTransit normally wraps messages in its own envelope. This adapter registers a
`System.Text.Json` converter that encodes/decodes the **canonical BabelQueue
envelope via the core codec** and switches the bus to MassTransit's **raw JSON**
serializer — so the bytes on the wire are exactly what every other BabelQueue SDK
produces and consumes.

```csharp
using BabelQueue.MassTransit;

builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.UseBabelQueueEnvelopes();   // canonical envelope on the wire (raw JSON)
        cfg.ConfigureEndpoints(context);
    });
});

builder.Services.AddBabelQueuePublisher(defaultQueue: "orders");
```

## Produce

```csharp
using BabelQueue.MassTransit;

public class Orders(BabelQueuePublisher babelQueue)
{
    public Task Create() =>
        babelQueue.PublishAsync(
            "urn:babel:orders:created",
            new Dictionary<string, object?> { ["order_id"] = 1042L },
            "orders");
}
```

The queue receives the canonical envelope:

```json
{
  "job": "urn:babel:orders:created",
  "trace_id": "…",
  "data": { "order_id": 1042 },
  "meta": { "id": "…", "queue": "orders", "lang": "dotnet", "schema_version": 1, "created_at": 1749132727000 },
  "attempts": 0
}
```

## Consume

A consumer receives the decoded `Envelope`; route by URN:

```csharp
using BabelQueue;
using MassTransit;

public class OrderConsumer : IConsumer<Envelope>
{
    public Task Consume(ConsumeContext<Envelope> context)
    {
        var envelope = context.Message;
        if (!EnvelopeCodec.Accepts(envelope))
            return Task.CompletedTask;

        switch (EnvelopeCodec.Urn(envelope))
        {
            case "urn:babel:orders:created":
                // handle envelope.Data, envelope.TraceId …
                break;
        }
        return Task.CompletedTask;
    }
}
```

## OpenTelemetry `traceparent` propagation (ADR-0028)

For true cross-hop **span** parent-child linkage, the active producer span's W3C `traceparent` rides
on `SendContext.Headers` — MassTransit's native per-message metadata channel — **beside** the
canonical envelope (never inside it). Produce with the header-aware overload; `BabelQueue.Core`'s
`Telemetry.PublishAsync` fills the carrier:

```csharp
using BabelQueue.Tracing;

var headers = new Dictionary<string, string>();
await Telemetry.PublishAsync("urn:babel:orders:created", data, headers,
    env => babelQueue.PublishWithHeadersAsync("urn:babel:orders:created", data, headers, "orders"));
```

A consumer reads them back from `ConsumeContext.Headers` and starts its span as a child:

```csharp
public Task Consume(ConsumeContext<Envelope> context)
{
    var headers = MassTransitHeaders.Extract(context.Headers);
    return Telemetry.Wrap(async env => { /* ... */ }, headers)(context.Message);
}
```

With no `traceparent` the consumer falls back to the v0.1 `trace_id` mapping; a header-less send is
identical to `PublishAsync`. Requires `BabelQueue.Core 1.4.0`.

## Standalone converter

The converter works with any `System.Text.Json` usage (not just MassTransit):

```csharp
var options = new JsonSerializerOptions().AddBabelQueueEnvelopes();
string wire = JsonSerializer.Serialize(envelope, options); // == EnvelopeCodec.Encode(envelope)
```

## License

[MIT](LICENSE) © Muhammet Şafak
