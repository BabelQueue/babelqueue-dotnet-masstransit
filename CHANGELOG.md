# Changelog

All notable changes to `BabelQueue.MassTransit` are documented here. The format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). The envelope wire
format is versioned separately by `meta.schema_version` (currently **1**).

## [Unreleased]

## [1.0.0] - 2026-06-07

**1.0.0 — the public API is now SemVer-stable**: breaking changes require a MAJOR,
following the deprecation policy. The wire envelope is unchanged
(`schema_version: 1`). Full reference at [babelqueue.com](https://babelqueue.com).

### Changed
- Require `BabelQueue.Core 1.0.0`.

### Internal
- CI enforces **Roslyn analyzers** (`AnalysisLevel=latest-recommended`, warnings as
  errors) and a **coverlet line-coverage gate** (`/p:Threshold=90`). Added DI /
  config tests (incl. an in-memory-bus `UseBabelQueueEnvelopes` test) — line
  coverage is 100%.

## [0.1.0] - 2026-06-06

### Added
- `BabelEnvelopeJsonConverter` — a `System.Text.Json` converter that encodes/decodes
  the canonical BabelQueue envelope via the core `EnvelopeCodec`, byte-for-byte. Used
  by MassTransit's raw JSON serializer (or any STJ usage).
- `UseBabelQueueEnvelopes()` — bus configuration that registers the converter and
  switches MassTransit to raw JSON, so the wire body is the canonical envelope.
- `AddBabelQueueEnvelopes(JsonSerializerOptions)` — add the converter to any options.
- `BabelQueuePublisher` — ergonomic producer over `ISendEndpointProvider`
  (`PublishAsync(urn, data, queue)`); `AddBabelQueuePublisher()` DI helper.

### Notes
- Pre-1.0: the public API may change before the `1.0.0` tag.
- Built on `BabelQueue.Core`; targets .NET 8 and MassTransit 8+.

[Unreleased]: https://github.com/BabelQueue/babelqueue-dotnet-masstransit/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/BabelQueue/babelqueue-dotnet-masstransit/compare/v0.1.0...v1.0.0
[0.1.0]: https://github.com/BabelQueue/babelqueue-dotnet-masstransit/releases/tag/v0.1.0
