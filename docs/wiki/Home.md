# Lite.Serialization.Protobuf

A **code-first, source-generator-driven Protobuf serializer** for plain C# types — no attributes,
no `.proto` required. It serializes any POCO (class, struct, record class, record struct, readonly
record struct), produces standard protobuf wire bytes, and plugs straight into gRPC.NET via a
`Grpc.Core.Marshaller<T>`.

**Scope:** this package serializes **messages**. gRPC services, contracts, streaming and clients are
your own concern — Lite gives you the marshaller and the message types, you wire the service.

## Why

- **Zero ceremony** — call `LiteSerializer.Serialize<T>(in value)` on a normal class; the source
  generator emits a specialized serializer at compile time (no reflection, no runtime model build).
- **Fast & low-alloc** — exact-size two-pass writing, pooled buffers, and zero-allocation
  `SerializeTo(Span)`. Competitive with (often faster than) Google.Protobuf and protobuf-net.
- **Real protobuf** — bytes are wire-compatible with Google.Protobuf when field numbers match.
- **Both directions** — code-first (POCO → `.proto`) and proto-first (`.proto` → C#) via the CLI.

## Quick links

- [Getting Started](Getting-Started.md) — install and first round-trip
- [Wire Compatibility](Wire-Compatibility.md) — interop with Google.Protobuf, type mapping
- [Fluent Configuration](Fluent-Configuration.md) — override tags, names, ignore fields
- [gRPC](gRPC.md) — code-first gRPC.NET with Lite marshallers
- [CLI](CLI.md) — `lite-proto export` / `import`
- [Conventions](Conventions.md) — type mapping, field tags, supported shapes
- [Practices](Practices.md) — field numbers, struct vs class, schema evolution
- [Dependency Injection](Dependency-Injection.md) — using Lite with gRPC.NET DI
- [Benchmarks](Benchmarks.md) — numbers and how to run them
