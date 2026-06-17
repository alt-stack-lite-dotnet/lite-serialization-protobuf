# Wire Compatibility

Lite emits standard protobuf3 wire bytes. A Lite POCO and a Google.Protobuf message interoperate
**when their field numbers and wire types match**.

## Field numbers matter

By default Lite derives a field tag from the property name (FNV-1a hash). That is stable for a given
name but does **not** match a hand-written `.proto`'s `1, 2, 3…`. For interop (and for compact tags),
pin the numbers using the Grpc.Tools `const` convention:

```csharp
public sealed class User
{
    public const int IdFieldNumber = 1;
    public long Id { get; set; }

    public const int NameFieldNumber = 2;
    public string Name { get; set; } = "";
}
```

Now `User` is byte-for-byte interchangeable with this `.proto`:

```proto
message User { int64 id = 1; string name = 2; }
```

```csharp
// Lite → Google
Span<byte> buf = stackalloc byte[LiteSerializer.ComputeSize(in user)];
int n = LiteSerializer.SerializeTo(in user, buf);
var google = ProtoUser.Parser.ParseFrom(buf[..n]);

// Google → Lite
var lite = LiteSerializer.DeserializeFrom<User>(google.ToByteArray());
```

See `WireCompatTests` for the full bidirectional + size-parity coverage.

## Type mapping

| C# type | proto type | wire | Google-compatible |
|---|---|---|:--:|
| `int` | `int32` | varint | ✅ |
| `long` | `int64` | varint | ✅ |
| `uint` | `uint32` | varint | ✅ |
| `ulong` | `uint64` | varint | ✅ |
| `bool` | `bool` | varint | ✅ |
| `float` | `float` | fixed32 | ✅ |
| `double` | `double` | fixed64 | ✅ |
| `string` | `string` | length-delimited | ✅ |
| `byte[]` | `bytes` | length-delimited | ✅ |
| `byte` / `sbyte` / `short` / `ushort` | `uint32`/`int32` (widened) | varint | ✅ |
| `enum` | `enum` | varint | ✅ |
| `List<T>` / `IList<T>` / `IReadOnlyList<T>` / `T[]` | `repeated` | packed (scalars) | ✅ |
| `Dictionary<K,V>` / `IReadOnlyDictionary<K,V>` | `map<K,V>` | length-delimited entries | ✅ |
| nested POCO | `message` | length-delimited | ✅ |
| `Guid` | `bytes` (16) | length-delimited | ⚠️ Lite-specific |
| `DateTime` | `int64` (UTC ticks) | varint | ⚠️ Lite-specific |
| `decimal` | `bytes` (16) | length-delimited | ⚠️ Lite-specific |
| `T?` (nullable value) | underlying type | — | ✅ (null ⇒ omitted) |

`Guid` / `DateTime` / `decimal` use Lite's own encodings — they round-trip perfectly through Lite but
have no standard protobuf equivalent, so don't expect Google to interpret those fields.

> Lite never emits `sint*` (zigzag) or `fixed*`/`sfixed*` integer variants. A `.proto` you intend to
> share must use `int32/int64/uint32/uint64` for the matching C# integer types.

## proto3 semantics Lite honors

- **Default values are omitted** — a `0` / `""` / `false` / default-enum field produces no bytes.
- **Field order on the wire is irrelevant** — readers accept any order.
- **Unknown fields are skipped** — add fields freely; old consumers ignore new ones.
- **Missing fields read as default** — drop a field and new consumers see the default.

These give you forward/backward schema evolution (see `CompatTests`).
