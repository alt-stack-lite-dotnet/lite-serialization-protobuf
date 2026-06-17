# Conventions

How Lite maps C# to protobuf, with no attributes.

## Supported type kinds

`class`, `struct`, `record class`, `record struct`, `readonly record struct`. Mutable types are filled
property-by-property; positional records are built through their primary constructor. The generator
picks constructor-mode vs setter-mode automatically based on what is settable vs init-only.

## Type mapping

| C# | proto | notes |
|---|---|---|
| `int` / `long` / `uint` / `ulong` | int32 / int64 / uint32 / uint64 | varint |
| `short` / `ushort` / `byte` / `sbyte` | int32 / uint32 | widened |
| `bool` | bool | |
| `float` / `double` | float / double | fixed32 / fixed64 |
| `string` | string | UTF-8 |
| `byte[]` | bytes | |
| `enum` | enum | varint |
| `Guid` | bytes (16) | Lite-specific encoding |
| `DateTime` | int64 | UTC ticks, Lite-specific |
| `decimal` | bytes (16) | lossless via `decimal.GetBits()`, Lite-specific |
| `T?` (nullable value) | underlying | null ⇒ omitted |
| nested POCO | message | length-delimited |
| `List<T>` / `T[]` / `IList<T>` / `IReadOnlyList<T>` | repeated | packed for scalars |
| `Dictionary<K,V>` / `IDictionary` / `IReadOnlyDictionary` | map<K,V> | proto3 key types |

See [Wire Compatibility](Wire-Compatibility.md) for which of these are byte-compatible with
Google.Protobuf (standard scalars, repeated, map, enum, nested) versus Lite-only (`Guid`, `DateTime`,
`decimal`).

## Field tags

Precedence: fluent `.Tag(n)` > `const int XxxFieldNumber` > FNV-1a name-hash. The name-hash is
deterministic and stable across adding/reordering fields, but changes if you **rename** a field — so
pin explicit numbers for anything on the wire.

## Proto names

Member names are emitted as `snake_case` (`PrimaryAddress` → `primary_address`). Override with
`.Name("...")` in a [fluent config](Fluent-Configuration.md).

## proto3 semantics

- Default values (`0`, `""`, `false`, default enum) are omitted from the wire.
- Fields may appear in any order; the reader accepts any order.
- Unknown fields are skipped; missing fields read as default. (Forward/backward compatible.)

## Not supported

- `char`, `DateTimeOffset`, `TimeSpan`, arbitrary structs without a proto mapping.
- Open generics at the call site — `LiteSerializer.SerializeTo<T>(...)` needs a concrete `T` (a C# 12
  interceptor constraint). A `Helper<T>()` wrapper will not be intercepted.
- `sint*` / `fixed*` / `sfixed*` integer encodings (Lite picks one mapping per C# type).
