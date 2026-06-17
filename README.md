# Lite.Serialization.Protobuf

A code-first Protobuf serializer for .NET. A source generator emits a specialized serializer for each
plain POCO — `class`, `struct`, `record class`, `record struct`, `readonly record struct` — with no
attributes, no hand-written `.proto`, and no `IMessage` base type. Output is standard protobuf3 wire
format and integrates with gRPC.NET through a `Marshaller<T>`.

- **Span-first API.** Serialize into a caller-provided buffer (no allocation); deserialize from a span.
- **No reflection, no runtime model.** The serializer is generated at compile time; calls are wired in
  via C# 12 interceptors (no interface dispatch on the hot path).
- **Schema is derived, not authored.** A `.proto` is generated alongside each type for cross-language
  interop; the CLI exports it (POCO → `.proto`) and imports the other direction (`.proto` → POCO).

Targets `net10.0`; requires a C# 12+ toolchain (interceptors).

## Install

```bash
dotnet add package Lite.Serialization.Protobuf
```

The package's MSBuild props/targets register the generated interceptor namespace automatically — no
manual `<InterceptorsNamespaces>` is required.

## Quick start

```csharp
using Lite.Serialization.Protobuf;

public sealed class User
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public List<string> Tags { get; set; } = new();
}

var user = new User { Id = 42, Name = "Ada", Tags = { "admin" } };

// Serialize into a buffer you own.
Span<byte> buffer = stackalloc byte[LiteSerializer.ComputeSize(in user)];
int written = LiteSerializer.SerializeTo(in user, buffer);

// Deserialize from a span.
User copy = LiteSerializer.DeserializeFrom<User>(buffer[..written]);
```

The type argument must be concrete at the call site (a C# interceptor constraint); calls through an
open generic `T` are not intercepted.

## API

```csharp
// Serialize — the caller provides the destination (no byte[]-returning overload exists).
int  LiteSerializer.SerializeTo<T>(in T value, Span<byte> destination);     // returns bytes written
void LiteSerializer.SerializeTo<T>(in T value, IBufferWriter<byte> writer);
RentedBuffer LiteSerializer.SerializeRented<T>(in T value);                 // pool-backed; dispose it
int  LiteSerializer.ComputeSize<T>(in T value);

// Deserialize
T LiteSerializer.DeserializeFrom<T>(ReadOnlySpan<byte> source);
T LiteSerializer.DeserializeFrom<T>(ReadOnlySequence<byte> source);

// gRPC
IProtoSerializer<T> LiteSerializer.For<T>();
Marshaller<T>       LiteSerializer.MarshallerFor<T>();
```

There is no `byte[]`-returning serialize method: `Span<byte>` cannot escape the method that allocates
it, so owned bytes come from a buffer you provide (`SerializeTo`) or one rented from the pool
(`SerializeRented`).

## Supported types

| C# | proto | Notes |
|---|---|---|
| `int` / `uint` / `long` / `ulong` / `short` / `ushort` / `byte` / `sbyte` | int32 / uint32 / int64 / uint64 | widened where needed |
| `bool`, `float`, `double`, `string`, `byte[]` | bool, float, double, string, bytes | |
| `enum` | enum | varint |
| `T?` (nullable value type) | underlying type | null is omitted |
| nested POCO | message | |
| `List<T>` / `T[]` / `IList<T>` / `IReadOnlyList<T>` | repeated | packed for scalars |
| `Dictionary<K,V>` / `IDictionary` / `IReadOnlyDictionary` | map<K,V> | |
| `Guid`, `DateTime`, `decimal` | bytes(16) / int64 / bytes(16) | Lite-specific encodings, not standard-proto |

`Guid` / `DateTime` / `decimal` round-trip through Lite but are not wire-compatible with other
protobuf implementations. Everything else is. For wire/gRPC use, pin field numbers with a
`const int XxxFieldNumber` (the Grpc.Tools convention) or a fluent config; otherwise tags are derived
from a deterministic name hash (stable across adding/reordering fields, but not across renames).

## Performance

Benchmarked against `Google.Protobuf` (Grpc.Tools-generated `IMessage`) and `protobuf-net`. Numbers
below are from the in-process micro-bench (`-- quick`) and are **machine-dependent** — run it yourself.
The notable property is that `SerializeTo` writes into a caller buffer and so allocates nothing, while
both competitors return a freshly allocated array.

| Scenario | Google.Protobuf | protobuf-net | Lite (`SerializeTo`) |
|---|--:|--:|--:|
| Small serialize | 156 ns / 152 B | 476 ns / 432 B | 93 ns / 0 B |
| Small deserialize | 345 ns / 552 B | 513 ns / 464 B | 230 ns / 376 B |
| Medium serialize | 571 ns / 208 B | 799 ns / 480 B | 201 ns / 0 B |
| Medium deserialize | 652 ns / 1176 B | 1492 ns / 872 B | 459 ns / 904 B |
| Large serialize | 98.6 µs / 26.6 KB | 127 µs / 92 KB | 48.7 µs / 0 B |
| Large deserialize | 110 µs / 170 KB | 169 µs / 136 KB | 73 µs / 162 KB |

`Google.Protobuf` represents every message as a `class`; Lite can serialize value-type messages
(`struct` / `readonly record struct`) with no allocation for the message instance.

For stable statistics use the BenchmarkDotNet harness (3 launches × 20 iterations + `MemoryDiagnoser`):

```bash
dotnet run -c Release --project benchmark/Lite.Serialization.Protobuf.Benchmarks
```

`-- quick` runs the lightweight in-process variant; `-- sizes` prints the wire-size comparison.

## How it works

1. The generator finds `LiteSerializer.*<T>(...)` call sites and collects the distinct concrete `T`s.
2. For each `T` it walks the nested POCO fields (transitive closure).
3. It emits a `<TypeName>__ProtoSerializer` with static `WriteToSpan` / `ComputeSize` / `ReadFromSpan`.
4. It emits C# 12 `[InterceptsLocation]` interceptors so each `LiteSerializer.*<T>(...)` call binds
   directly to the generated static method — no interface dispatch.
5. It emits an assembly attribute carrying the `.proto` text, read back via `ProtoSchemaRegistry`.

## Limitations

- `T` must be a concrete type at the call site; generic wrappers are not intercepted. Obtain an
  `IProtoSerializer<T>` from `For<T>()` at a concrete boundary if you need to pass it around.
- `Guid` / `DateTime` / `decimal` use Lite-specific encodings (see above).
- Name-hash field tags are not stable across renames — pin explicit field numbers for wire formats.

## Documentation

The [wiki](docs/wiki/Home.md) covers wire compatibility, the CLI, gRPC integration, fluent
configuration, conventions, and benchmarks in detail.

## Build

```bash
dotnet build Lite.Serialization.Protobuf.sln -c Release
dotnet test  Lite.Serialization.Protobuf.sln -c Release
```

## Status

`1.0.0-rc-1` — release candidate; the API is stabilizing.
