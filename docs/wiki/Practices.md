# Practices

Guidance for getting the most out of Lite.

## Pin field numbers for anything on the wire

The default name-hash tags are fine for local storage, but they change if you rename a field, and they
don't match a hand-written `.proto`. For gRPC, persisted data, or cross-service messages, set explicit
numbers — either the const convention or a fluent config:

```csharp
public sealed class User
{
    public const int IdFieldNumber = 1;   public long Id { get; set; }
    public const int NameFieldNumber = 2; public string Name { get; set; } = "";
}
```

This also makes the wire byte-identical to Google.Protobuf (see [Wire Compatibility](Wire-Compatibility.md)).

## Choose struct vs class deliberately

Lite serializes any kind — use that. A `struct` / `readonly record struct` message has **no heap
allocation for the instance itself**, which Google.Protobuf cannot offer (its messages are always
classes). Good for small, hot-path messages:

```csharp
public readonly record struct Tick(long Symbol, double Price, long Ts)
{
    public const int SymbolFieldNumber = 1;
    public const int PriceFieldNumber = 2;
    public const int TsFieldNumber = 3;
}
```

Use a `class` for large messages, shared mutable state, or deep graphs where copying a big struct
would cost more than it saves.

## Reach for the zero-allocation path on hot loops

`Serialize → byte[]` allocates exactly the result array. To allocate nothing, provide the buffer:

```csharp
// small — on the stack
Span<byte> buf = stackalloc byte[256];
int n = LiteSerializer.SerializeTo(in value, buf);

// large — from a pool
byte[] rented = ArrayPool<byte>.Shared.Rent(LiteSerializer.ComputeSize(in value));
try   { int n = LiteSerializer.SerializeTo(in value, rented); /* use rented[..n] */ }
finally { ArrayPool<byte>.Shared.Return(rented); }
```

`SerializeRented` returns a pool-backed `RentedBuffer` if you'd rather Lite manage the rent (dispose it).

## Prefer immutable messages

`record` / `record struct` with a primary constructor deserialize in one shot (constructor-mode), which
avoids half-initialized instances and matches message/DTO semantics. Mutable classes work too — the
generator fills settable members.

## Evolve schemas additively

Protobuf's compatibility holds only if numbers are stable:

- **Add** fields with **new** numbers — old readers skip them, new readers default missing ones.
- **Never** reuse or renumber an existing field — that silently reinterprets bytes.
- Removing a field is fine; don't recycle its number later.

## Keep `T` concrete at the call site

`LiteSerializer.Serialize<T>(...)` is intercepted by field number at the call site, so `T` must be a
concrete type there. A generic wrapper `void Send<T>(T v) => LiteSerializer.Serialize(in v)` won't be
intercepted (a C# 12 interceptor limitation). Call with the concrete type, or expose
`IProtoSerializer<T>` from `For<T>()` at a concrete boundary.
