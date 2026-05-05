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

Бенч против `Google.Protobuf` (`Grpc.Tools`-сгенерированный класс с `IMessage`). Сообщение: 6 полей (long, 2 string, bool, int, list of 3 string).

### Serialize

| API | Time | vs Google | Memory |
|---|---:|---:|---:|
| Google.Protobuf `ToByteArray()` (baseline) | 175 ns | 1.00× | 152-336 B |
| `LiteSerializer.SerializeTo(in v, span)` (zero-alloc, struct) | **104 ns** | **0.60×** | **0 B** |
| `LiteSerializer.SerializeTo(in v, span)` (zero-alloc, class) | **128 ns** | **0.74×** | **0 B** |
| `LiteSerializer.SerializeRented(in v)` (pool, struct) | 158 ns | 0.91× | 24 B |
| `LiteSerializer.Serialize(in v) → byte[]` (struct) | **156 ns** | **0.90×** | 106 B |
| `LiteSerializer.Serialize(in v) → byte[]` (class) | 189 ns | 1.09× | 120 B |

### Deserialize

| API | Time | vs Google | Memory |
|---|---:|---:|---:|
| Google.Protobuf `Parser.ParseFrom()` (baseline) | 254 ns | 1.00× | 552 B |
| `LiteSerializer.Deserialize<RecordClass>` | **218 ns** | **0.85×** | 344 B (62%) |
| `LiteSerializer.Deserialize<Struct>` | **224 ns** | **0.88×** | ~250 B (46%) |
| `LiteSerializer.Deserialize<RecordStruct>` | **223 ns** | **0.88×** | ~250 B |
| `LiteSerializer.Deserialize<Class>` | 247 ns | 0.97× | 376 B (68%) |

**Везде: быстрее Google, и от 30% до 100% меньше памяти.**

Конфиг: BenchmarkDotNet v0.15.8, .NET 10.0.7, Intel Core i5-9300H, Windows 11. См. [`benchmark/`](benchmark/Lite.Serialization.Protobuf.Benchmarks/) для воспроизведения.

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

`1.0.0-alpha`. API стабилизируется. Welcome to file issues.

## Build

```bash
dotnet test Lite.Serialization.Protobuf.sln -c Release
dotnet run --project benchmark/Lite.Serialization.Protobuf.Benchmarks -c Release
```
