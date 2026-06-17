# Getting Started

## Install

```bash
dotnet add package Lite.Serialization.Protobuf
```

That is all the wiring you need. The package ships MSBuild props/targets (`buildTransitive/`) that
auto-register the generated interceptor namespace and the generator's compiler-visible properties — so
`LiteSerializer.*` calls are intercepted out of the box, with **no manual `<InterceptorsNamespaces>`**
in your `.csproj`.

> Requires a C# 12+ / .NET 8+ toolchain (interceptors). The package targets `net10.0`.

## First round-trip

Define a normal type — no attributes:

```csharp
public sealed class User
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
    public List<string> Tags { get; set; } = new();
}
```

Serialize and deserialize:

```csharp
using Lite.Serialization.Protobuf;

var user = new User { Id = 42, Name = "Ada", IsActive = true, Tags = { "admin" } };

// Serialize into a buffer you own — zero allocation:
Span<byte> buf = stackalloc byte[LiteSerializer.ComputeSize(in user)];
int n = LiteSerializer.SerializeTo(in user, buf);

// Read it back — zero-copy from the span:
User back = LiteSerializer.DeserializeFrom<User>(buf[..n]);
```

> **The type argument must be concrete at the call site.** The generator intercepts
> `LiteSerializer.SerializeTo<User>(...)`; it cannot intercept calls through an open generic `T`.

## Supported type kinds

The same data works as any of these:

```csharp
public class            UserClass(...) { ... }
public struct           UserStruct { ... }
public record class     UserRecord(long Id, string Name);
public record struct    UserRecordStruct(long Id, string Name);
public readonly record struct UserRo(long Id, string Name);
```

Mutable types are filled property-by-property; positional records are constructed via their primary
constructor.

## API at a glance

| Call | Purpose |
|---|---|
| `LiteSerializer.SerializeTo<T>(in v, Span<byte>)` | zero-alloc write into a caller buffer → bytes written |
| `LiteSerializer.SerializeTo<T>(in v, IBufferWriter<byte>)` | serialize into a buffer writer / pipeline |
| `LiteSerializer.SerializeRented<T>(in v)` | pool-backed `RentedBuffer` (dispose it) |
| `LiteSerializer.ComputeSize<T>(in v)` | exact serialized size in bytes |
| `LiteSerializer.DeserializeFrom<T>(...)` | from `ReadOnlySpan<byte>` (zero-copy) or `ReadOnlySequence<byte>` |
| `LiteSerializer.For<T>()` | get a reusable `IProtoSerializer<T>` |
| `LiteSerializer.MarshallerFor<T>()` | get a `Grpc.Core.Marshaller<T>` for gRPC.NET |

## Next

- Talking to Google.Protobuf code? See [Wire Compatibility](Wire-Compatibility.md).
- Wiring a gRPC service? See [gRPC](gRPC.md).
- Need to control field numbers/names? See [Fluent Configuration](Fluent-Configuration.md).
