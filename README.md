# Lite.Serialization.Protobuf

Code-first protobuf serializer for .NET. Source-generated, span-first, **faster than `Google.Protobuf` and protobuf-net — and serializes with zero allocation** — without `IMessage`, without `protoc`, without attributes.

## TL;DR

```csharp
// Plain POCO. No attributes. class, struct, record class, record struct, readonly record struct — all work.
public struct GetUserRequest
{
    public long UserId;
    public string Email;
    public bool IncludeDeleted;
}

// Hot path — zero allocation: you own the buffer (stackalloc small / ArrayPool large).
Span<byte> buf = stackalloc byte[LiteSerializer.ComputeSize(in request)];
int n = LiteSerializer.SerializeTo(in request, buf);

// Or pool-backed, if you want Lite to manage the rent:
using var rented = LiteSerializer.SerializeRented(in request);
Send(rented.Span);

// Read back — zero-copy from a span:
var copy = LiteSerializer.DeserializeFrom<GetUserRequest>(buf[..n]);

// gRPC interop:
var marshaller = LiteSerializer.MarshallerFor<GetUserRequest>();
new Method<GetUserRequest, GetUserResponse>(MethodType.Unary, ..., marshaller, ...);
```

There is **no `byte[]`-returning API by design** — serialization writes into a caller buffer (`SerializeTo`) or a pooled one (`SerializeRented`). The `.proto` schema is generated automatically alongside the serializer (interop with other languages) and read via `ProtoSchemaRegistry`.

## Why

Стандартный путь .NET-разработчика — `.proto` → `protoc` → C# DTO от `Grpc.Tools`. Боль:

- **DTO всегда классы** — никогда структуры. На горячем пути GC давит.
- **Сгенерённый код корявый**, partial-расширять неудобно.
- **`.proto` — источник правды**, рефакторить можно только через него.

Эта либа переворачивает: **C# — источник, `.proto` — производное, маршаллер — производное**. На gRPC цепляется через C# 12 `[InterceptsLocation]`, без runtime overhead.

## Performance

Бенч против `Google.Protobuf` (IMessage от `Grpc.Tools`) и `protobuf-net`. Три формы payload (Small / Medium / Large), пять C#-видов типа. Полная таблица — в [вики → Benchmarks](docs/wiki/Benchmarks.md).

### Lite быстрее во всём

Во сколько раз `SerializeTo` / `DeserializeFrom` быстрее `Google.Protobuf` (длиннее — быстрее, `1.0×` = Google):

```
Medium serialize     2.8×  ████████████████████████████
Large  serialize     2.0×  ████████████████████
Small  serialize     1.7×  █████████████████
Small  deserialize   1.5×  ███████████████
Large  deserialize   1.5×  ███████████████
Medium deserialize   1.4×  ██████████████
                           ╵─────────╵ Google 1.0×
```

### И сериализует с НУЛЁМ аллокаций

`SerializeTo` пишет в твой буфер — 0 байт на вызов. Конкуренты возвращают свежий массив каждый раз. Serialize, bytes/op (короче — лучше):

```
Lite  (SerializeTo)                          0 B
Google.Protobuf   ██████          152 B  (small) … 26.6 KB (large)
protobuf-net      ████████████████ 432 B  (small) … 92 KB   (large)
```

### Самый сок

| Сценарий | Google (IMessage) | protobuf-net | **Lite** | Lite vs Google |
|---|---:|---:|---:|:---:|
| Small serialize (`SerializeTo`) | 156 ns / 152 B | 476 ns / 432 B | **93 ns / 0 B** | **1.7× + ноль аллокаций** |
| Small deserialize | 345 ns / 552 B | 513 ns / 464 B | **230 ns / 376 B** | **1.5× быстрее** |
| Medium serialize (`SerializeTo`) | 571 ns / 208 B | 799 ns / 480 B | **201 ns / 0 B** | **2.8× + ноль аллокаций** |
| Medium deserialize | 652 ns / 1176 B | 1492 ns / 872 B | **459 ns / 904 B** | **1.4× быстрее** |
| Large serialize (`SerializeTo`) | 98.6 µs / 26.6 KB | 127 µs / 92 KB | **48.7 µs / 0 B** | **2.0× + ноль аллокаций** |
| Large deserialize | 110 µs / 170 KB | 169 µs / 136 KB | **73 µs / 162 KB** | **1.5× быстрее** |

### Структуры — то, что Google.Protobuf не умеет

`Google.Protobuf` всегда генерит классы (`IMessage`) — каждое сообщение это heap-объект. Lite сериализует **struct / record struct / readonly record struct** напрямую, без аллокации объекта-сообщения. Small shape, `SerializeTo` (ns/op, все — **0 B**):

| Тип | ns/op | B/op |
|---|---:|---:|
| Google.Protobuf (class, IMessage) → byte[] | 156 | 152 |
| Lite `class` | 93 | **0** |
| Lite `struct` | 89 | **0** |
| Lite `record class` | 88 | **0** |
| Lite `record struct` | 88 | **0** |
| Lite `readonly record struct` | 89 | **0** |

### Буфер выбираешь ты

Нужны владеемые байты — выдели буфер сам: `stackalloc` для мелких, `ArrayPool` для крупных. Сериализация при этом не аллоцирует вообще.

```csharp
// мелкие — на стеке, ноль аллокаций
Span<byte> buf = stackalloc byte[256];
int n = LiteSerializer.SerializeTo(in value, buf);
Send(buf[..n]);

// крупные — из пула, ноль аллокаций на вызов
var rented = ArrayPool<byte>.Shared.Rent(LiteSerializer.ComputeSize(in value));
try { int n = LiteSerializer.SerializeTo(in value, rented); Send(rented.AsSpan(0, n)); }
finally { ArrayPool<byte>.Shared.Return(rented); }
```

Цифры — in-process микробенч (`-- quick`), машинозависимы. Строгий прогон (BenchmarkDotNet, 3 launch × 20 iter + MemoryDiagnoser): `dotnet run -c Release --project benchmark/Lite.Serialization.Protobuf.Benchmarks`. См. [`benchmark/`](benchmark/Lite.Serialization.Protobuf.Benchmarks/).

## API surface

### Высокоуровневое (через интерцепторы — компилятор подменяет на прямой вызов)

```csharp
// Serialize — span-first, no byte[]
int  LiteSerializer.SerializeTo<T>(in T value, Span<byte> destination);        // zero-alloc, returns bytes written
void LiteSerializer.SerializeTo<T>(in T value, IBufferWriter<byte> writer);    // pipelines / gRPC sinks
RentedBuffer LiteSerializer.SerializeRented<T>(in T value);                    // pool-backed; using-scope dispose
int  LiteSerializer.ComputeSize<T>(in T value);                               // size the buffer

// Deserialize
T LiteSerializer.DeserializeFrom<T>(ReadOnlySpan<byte> source);                // zero-copy
T LiteSerializer.DeserializeFrom<T>(ReadOnlySequence<byte> source);            // multi-segment / gRPC payload

// gRPC bridge
IProtoSerializer<T> LiteSerializer.For<T>();
Marshaller<T>       LiteSerializer.MarshallerFor<T>();
Marshaller<T>       LiteSerializer.CreateMarshaller<T>(IProtoSerializer<T>);
```

`T` обязан быть **конкретным** типом в call-site (это ограничение C# 12 `[InterceptsLocation]`).

### Низкоуровневое

```csharp
public interface IProtoSerializer<T>
{
    void WriteTo(in T value, IBufferWriter<byte> writer);
    T ReadFrom(ReadOnlySequence<byte> source);
}

// Generated per type:
internal sealed class MyDto__ProtoSerializer : IProtoSerializer<MyDto>
{
    public static readonly MyDto__ProtoSerializer Instance = ...;
    public static readonly Marshaller<MyDto> Marshaller = ...;

    public static void WriteTo(in MyDto value, IBufferWriter<byte> writer);
    public static int  WriteToSpan(in MyDto value, Span<byte> destination);
    public static int  ComputeSize(MyDto value);
    public static MyDto ReadFrom(ReadOnlySequence<byte> source);
    public static MyDto ReadFromSpan(ReadOnlySpan<byte> source);
}
```

### Fluent override (опционально)

По умолчанию proto-теги генерятся **детерминированным name-hash** (FNV-1a) — стабильны при добавлении/перестановке полей, ломаются только при ренейме. Имена в `.proto` — `snake_case`. Если надо переопределить — реализуй `IProtoSerializerConfiguration<T>`:

```csharp
public class GetUserConfig : IProtoSerializerConfiguration<GetUserRequest>
{
    public void Configure(IProtoSerializerBuilder<GetUserRequest> b)
    {
        b.Field(x => x.UserId).Tag(7).Name("user_id");
        b.Field(x => x.Email).Ignore();
    }
}
```

## Type support

| C# тип | .proto | Notes |
|---|---|---|
| `int`, `uint`, `long`, `ulong`, `short`, `ushort`, `byte`, `sbyte` | int32/uint32/int64/uint64 | widened где надо |
| `bool` | bool | |
| `float`, `double` | float, double | |
| `string` | string | UTF-8 |
| `byte[]` | bytes | |
| `Guid` | bytes (16) | Lite-specific |
| `DateTime` | int64 (UTC ticks) | Lite-specific |
| `decimal` | bytes (16) — lossless через `decimal.GetBits()` | Lite-specific |
| `enum` | enum (varint) | |
| `T?` для value types | optional | null ⇒ omitted |
| Вложенный POCO-тип | message | length-delimited |
| `List<T>`, `T[]`, `IList<T>`, `IReadOnlyList<T>` | repeated | packed для скаляров |
| `Dictionary<K,V>`, `IDictionary<K,V>`, `IReadOnlyDictionary<K,V>` | map<K,V> | proto3-совместимые ключи |

## POCO formats

Все пять форм поддержаны автоматически:

```csharp
public class       Foo { public long X { get; set; } }   // mutable class
public struct      Foo { public long X; }                // mutable struct
public record class  Foo(long X);                         // record class (primary ctor)
public record struct Foo(long X);                         // record struct (primary ctor)
public readonly record struct Foo(long X);                // readonly record struct
```

SG автоматически выбирает между `new() + setters` и ctor-mode (`new T(p1, p2, ...)`) на основе того, что settable, что init-only.

## Streams

Streaming (server/client/bidi gRPC) — это уровень транспорта, **не сериализации**. Каждое сообщение в стриме — обычный proto message, его маршаллит наш `Marshaller<T>`. Стрим-wiring — забота вышестоящего слоя.

## Как работает

1. SG ищет вызовы `LiteSerializer.For<T>()` / `SerializeTo<T>(...)` / `DeserializeFrom<T>(...)` / etc. в твоём коде → собирает уникальные `T`.
2. Для каждого `T` рекурсивно обходит вложенные POCO-поля (transitive closure).
3. Эмитит `<TypeName>__ProtoSerializer` со static-методами `WriteToSpan` / `ComputeSize` / `ReadFromSpan`.
4. Эмитит C# 12 `[InterceptsLocation]` интерцепторы — на этапе компиляции вызовы `LiteSerializer.X<T>(...)` подменяются прямыми вызовами static-методов сгенерированного сериализатора (без interface-dispatch).
5. Эмитит assembly-attribute `[GeneratedProtoSchema("file.proto", "...")]` — аккумулируется по package, читается через `ProtoSchemaRegistry`.

## Ограничения

- `T` должен быть **конкретным типом** в call-site. Generic-обёртки `Helper<T>() { LiteSerializer.SerializeTo<T>(...); }` не работают (by-design ограничение interceptor'ов) — там получишь `IProtoSerializer<T>` через `For<T>()` на конкретной границе.
- `Guid` / `DateTime` / `decimal` — Lite-specific кодировки, не wire-совместимы с другими языками без конвенции.
- Tag stability: name-hash стабилен на добавление/перестановку, но **ломается на ренейме**. Для wire/gRPC ставь явные `const int XxxFieldNumber` или fluent `.Tag(N)`.

## Status

`1.0.0-rc-1`. API стабилизируется. Welcome to file issues.

## Build

```bash
dotnet test Lite.Serialization.Protobuf.sln -c Release
dotnet run --project benchmark/Lite.Serialization.Protobuf.Benchmarks -c Release
```
