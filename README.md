# Lite.Serialization.Protobuf

Code-first protobuf serializer for .NET. Source-generated, zero-overhead, **faster than `Google.Protobuf` and uses less memory** — without `IMessage`, without `protoc`, without attributes.

## TL;DR

```csharp
// Plain POCO. No attributes. Class, struct, record class, record struct — all 4 work.
public struct GetUserRequest
{
    public long UserId;
    public string Email;
    public bool IncludeDeleted;
}

// Hot path — zero allocation:
LiteSerializer.SerializeTo(in request, buffer);

// Or get a byte[] like Google.Protobuf:
byte[] bytes = LiteSerializer.Serialize(in request);

// Or pool-backed:
using var rented = LiteSerializer.SerializeRented(in request);
SendBytes(rented.Span);

// Read back:
var copy = LiteSerializer.Deserialize<GetUserRequest>(bytes);

// gRPC interop:
var marshaller = LiteSerializer.MarshallerFor<GetUserRequest>();
new Method<GetUserRequest, GetUserResponse>(MethodType.Unary, ..., marshaller, ...);
```

`.proto`-схема генерится автоматически рядом со сериализатором — для interop с другими языками. Дамп через `ProtoSchemaRegistry.DumpToDirectory(path)`.

## Why

Стандартный путь .NET-разработчика — `.proto` → `protoc` → C# DTO от `Grpc.Tools`. Боль:

- **DTO всегда классы** — никогда структуры. На горячем пути GC давит.
- **Сгенерённый код корявый**, partial-расширять неудобно.
- **`.proto` — источник правды**, рефакторить можно только через него.

Эта либа переворачивает: **C# — источник, `.proto` — производное, маршаллер — производное**. На gRPC цепляется через C# 12 `[InterceptsLocation]`, без runtime overhead.

## Performance

Бенч против `Google.Protobuf` (IMessage от `Grpc.Tools`) и `protobuf-net`. Три формы payload (Small / Medium / Large), пять C#-видов типа. Полная таблица — в [вики → Benchmarks](docs/wiki/Benchmarks.md).

### Lite быстрее во всём

Во сколько раз Lite быстрее `Google.Protobuf` (длиннее — быстрее, `1.0×` = Google):

```
Large deserialize    1.9×  ███████████████████
Medium serialize     1.7×  █████████████████
Small  deserialize   1.5×  ███████████████
Medium deserialize   1.4×  ██████████████
Small  serialize     1.2×  ████████████
Large  serialize     1.1×  ███████████
                           ╵─────────╵ Google 1.0×
```

Head-to-head, large payload deserialize (короче — быстрее):

```
Lite          ████████              82 µs
Google        ███████████████      159 µs
protobuf-net  █████████████████████ 213 µs
```

Аллокации на serialize, small payload (короче — лучше):

```
Lite           ███           88 B   (только результат; SerializeTo → 0 B)
Google         ██████       152 B
protobuf-net   █████████████ 432 B
```

### Самый сок

Lite **быстрее всех в каждом сценарии** — и serialize, и deserialize:

| Сценарий | Google (IMessage) | protobuf-net | **Lite** | Lite vs Google |
|---|---:|---:|---:|:---:|
| Medium serialize | 526 ns / 208 B | 955 ns / 480 B | **306 ns / 88 B** | **1.7× быстрее · 0.42× памяти** |
| Medium deserialize | 854 ns / 1176 B | 1982 ns / 872 B | **631 ns / 904 B** | **1.4× быстрее** |
| Large serialize | 99.6 µs / 26.6 KB | 148 µs / 92 KB | **87.5 µs / 26.6 KB** | **быстрее, минимум памяти** |
| Large deserialize | 159 µs / 170 KB | 213 µs / 136 KB | **82 µs / 162 KB** | **1.9× быстрее** |
| Small serialize (`SerializeTo`) | 223 ns / 152 B | 437 ns / 432 B | **114 ns / 0 B** | **0 аллокаций** |

> `Serialize → byte[]` аллоцирует ровно размер выхода (никакого overhead); `SerializeTo(span)` — **ноль**.

**Структуры — то, чего `Google.Protobuf` не умеет вовсе** (его сообщения всегда `class` + `IMessage`). Lite сериализует `struct` и `readonly record struct` за **152–172 ns / 88 B** — наравне с IMessage по времени, но без аллокации самого сообщения на горячем пути.

### Структуры — то, что Google.Protobuf не умеет

`Google.Protobuf` всегда генерит классы (`IMessage`) — каждое сообщение это heap-объект. Lite сериализует **struct / record struct / readonly record struct** напрямую, без аллокации объекта-сообщения:

| Тип (Small serialize) | ns/op | B/op |
|---|---:|---:|
| Google.Protobuf (class, IMessage) | 296 | 152 |
| Lite `class` | 219 | 88 |
| Lite `struct` | 196 | 88 |
| Lite `record struct` | 225 | 88 |
| Lite `readonly record struct` | 228 | 88 |

### Аллокации: буфер выбираешь ты

`Serialize → byte[]` выделяет **ровно один** массив — тот, что возвращает (payload + 24 B заголовка массива .NET, без всякого overhead). Нужен ноль аллокаций — отдай свой буфер в `SerializeTo`: `stackalloc` для мелких, `ArrayPool` для крупных. Один и тот же Small-объект:

| Путь | ns/op | B/op |
|---|---:|---:|
| Google.Protobuf → `byte[]` | 261 | 152 |
| protobuf-net → `byte[]` | 461 | 432 |
| **Lite → `byte[]`** (выделяет только результат) | **170** | **88** |
| Lite `SerializeTo` + `stackalloc` (мелкие) | 107 | **0** |
| Lite `SerializeTo` + `ArrayPool` (крупные) | 130 | **0** |

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

### Высокоуровневое (через интерцепторы — компилятор сам подменяет на прямой вызов)

```csharp
// Serialize
void  LiteSerializer.Serialize<T>(in T value, IBufferWriter<byte> writer);
byte[] LiteSerializer.Serialize<T>(in T value);                          // exact-size byte[]
int   LiteSerializer.SerializeTo<T>(in T value, Span<byte> destination); // zero-alloc, returns bytes written
RentedBuffer LiteSerializer.SerializeRented<T>(in T value);              // pool-backed; using-scope dispose

// Size hint
int LiteSerializer.ComputeSize<T>(in T value);

// Deserialize
T LiteSerializer.Deserialize<T>(ReadOnlySequence<byte> source);
T LiteSerializer.Deserialize<T>(ReadOnlySpan<byte> source);
T LiteSerializer.Deserialize<T>(byte[] source);

// gRPC bridge
IProtoSerializer<T> LiteSerializer.For<T>();                  // for manual use
Marshaller<T>       LiteSerializer.MarshallerFor<T>();        // for Method<T1,T2>
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
| `Guid` | bytes (16) | |
| `DateTime` | int64 (UTC ticks) | |
| `decimal` | bytes (16) — lossless через `decimal.GetBits()` | |
| `enum` | enum (varint) | |
| `T?` для value types | optional | |
| Вложенный `[GrpcMessage]`-тип | message | length-delimited |
| `List<T>`, `T[]`, `IList<T>`, `IReadOnlyList<T>` | repeated | packed для скаляров |
| `Dictionary<K,V>`, `IDictionary<K,V>`, `IReadOnlyDictionary<K,V>` | map<K,V> | proto3-совместимые ключи |

## POCO formats

Все 4 формы поддержаны автоматически:

```csharp
public class       Foo { public long X { get; set; } }                  // mutable class
public struct      Foo { public long X; }                               // mutable struct
public record class  Foo(long X);                                       // record class with primary ctor
public record struct Foo(long X);                                       // record struct with primary ctor
```

SG автоматически выбирает между `new() + setters` и ctor-mode (`new T(p1, p2, ...)`) на основе того, что settable, что init-only.

## Streams

Streaming (server/client/bidi gRPC) — это уровень транспорта, **не сериализации**. Каждое сообщение в стриме — обычный proto message, его маршаллит наш `Marshaller<T>`. Стрим-wiring — забота вышестоящего слоя.

## Как работает

1. SG ищет вызовы `LiteSerializer.For<T>()` / `Serialize<T>(...)` / etc. в твоём коде → собирает уникальные `T`.
2. Для каждого `T` рекурсивно обходит вложенные POCO-поля (transitive closure).
3. Эмитит `<TypeName>__ProtoSerializer` со static-методами `WriteToSpan` / `ComputeSize` / `ReadFromSpan`.
4. Эмитит C# 12 `[InterceptsLocation]` интерцепторы — на этапе компиляции вызовы `LiteSerializer.X<T>(...)` подменяются прямыми вызовами static-методов сгенерированного сериализатора.
5. Эмитит assembly-attribute `[GeneratedProtoSchema("file.proto", "...")]` — аккумулируется по package, дампится через `ProtoSchemaRegistry.DumpToDirectory`.

## Ограничения

- `T` должен быть **конкретным типом** в call-site. Generic-обёртки `Helper<T>() { LiteSerializer.For<T>(); }` не работают (это by-design ограничение interceptor'ов).
- `decimal` — 16 байт через `GetBits()`. Не interop-совместимо с другими языками без конвенции; напиши converter если надо.
- Tag stability: name-hash стабилен на добавление/перестановку, но **ломается на ренейме**. В production используй fluent override с явными `.Tag(N)`.

## Status

`1.0.0-rc-1`. API стабилизируется. Welcome to file issues.

## Build

```bash
dotnet test Lite.Serialization.Protobuf.sln -c Release
dotnet run --project benchmark/Lite.Serialization.Protobuf.Benchmarks -c Release
```
