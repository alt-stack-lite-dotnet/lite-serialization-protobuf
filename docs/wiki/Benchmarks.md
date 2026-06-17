# Benchmarks

Benchmarks live in `benchmark/Lite.Serialization.Protobuf.Benchmarks` and compare Lite against
**Google.Protobuf** (IMessage) and **protobuf-net**, across payload shapes (Small / Medium / Large)
and all five C# type kinds. They are **not** part of the shipped package.

## Run

```bash
# Rigorous BenchmarkDotNet run (3 launches x 8 warmup x 20 iterations + MemoryDiagnoser)
dotnet run -c Release --project benchmark/Lite.Serialization.Protobuf.Benchmarks

# Filter to one set, or use a quick job
dotnet run -c Release --project benchmark/Lite.Serialization.Protobuf.Benchmarks -- --filter "*Medium*"
dotnet run -c Release --project benchmark/Lite.Serialization.Protobuf.Benchmarks -- --job short

# Lightweight in-process numbers (ns/op + bytes/op), no BDN toolchain required
dotnet run -c Release --project benchmark/Lite.Serialization.Protobuf.Benchmarks -- quick

# Wire-size comparison only
dotnet run -c Release --project benchmark/Lite.Serialization.Protobuf.Benchmarks -- sizes
```

## Wire size (bytes, lower is better)

All models use explicit field numbers, so Lite is byte-identical to Google.Protobuf.

| Payload | Google | protobuf-net | Lite |
|---|--:|--:|--:|
| Small (BenchUser) | 58 | 58 | **58** |
| Medium (nested + map) | 64 | 69 | **64** |
| Large (1000 items) | 26 525 | 27 522 | **26 525** |

## Throughput & allocations

Representative `-- quick` run (in-process; best of 5 passes). **Absolute numbers are
machine-dependent — run it yourself.** Lower is better. `ns` = nanoseconds/op, `B` = bytes
allocated/op.

Google.Protobuf and protobuf-net allocate a fresh `byte[]` on every serialize; Lite's `SerializeTo`
writes into a caller buffer — **0 B/op**.

| Scenario | Google (IMessage) | protobuf-net | Lite | Lite vs Google |
|---|--:|--:|--:|:--:|
| Small serialize (`SerializeTo`) | 156 ns / 152 B | 476 ns / 432 B | **93 ns / 0 B** | **1.7× · zero alloc** |
| Small deserialize | 345 ns / 552 B | 513 ns / 464 B | **230 ns / 376 B** | **1.5×** |
| Medium serialize (`SerializeTo`) | 571 ns / 208 B | 799 ns / 480 B | **201 ns / 0 B** | **2.8× · zero alloc** |
| Medium deserialize | 652 ns / 1176 B | 1492 ns / 872 B | **459 ns / 904 B** | **1.4×** |
| Large serialize (`SerializeTo`) | 98.6 µs / 26.6 KB | 127 µs / 92 KB | **48.7 µs / 0 B** | **2.0× · zero alloc** |
| Large deserialize | 110 µs / 170 KB | 169 µs / 136 KB | **73 µs / 162 KB** | **1.5×** |

### Type kinds (Small serialize, vs IMessage)

Google.Protobuf can only generate classes; Lite serializes any POCO kind, with no message-object
allocation on the hot path.

| Kind | ns/op | B/op |
|---|--:|--:|
| Google.Protobuf (class, IMessage) → byte[] | 156 | 152 |
| Lite `class` | 93 | **0** |
| Lite `struct` | 89 | **0** |
| Lite `record class` | 88 | **0** |
| Lite `record struct` | 88 | **0** |
| Lite `readonly record struct` | 89 | **0** |

### Reading the numbers

- Lite leads on **every** scenario — serialize and deserialize, small to large — and offers a true
  **zero-allocation** path (`SerializeTo` into a caller buffer → 0 B/op).
- **Serialize allocates nothing.** There is no `byte[]`-returning API — `SerializeTo` writes into a
  buffer you provide (`stackalloc` for small, `ArrayPool` for large), so the only allocation a serialize
  ever makes is none. Google.Protobuf and protobuf-net allocate a fresh array on every call.
- **Large serialize** allocates only the output size: nested/repeated messages and maps are written
  **directly into the destination span** with no per-element temporary buffer (this removed a former
  ~2× allocation overhead — the old `PooledBufferWriter`-per-element path).
- Packed fixed-width repeated fields (`float`/`double`) are deserialized into a **pre-sized** list
  (count = payload ÷ item size), avoiding growth reallocations.
- These are in-process numbers and a little noisy; use the rigorous BenchmarkDotNet run for stable
  stats. Lite's one remaining allocation gap vs protobuf-net is on large deserialize (162 vs 136 KB) —
  inherent object-graph + list growth — while still being ~2× faster.

## What we optimize for

- No reflection and no runtime model build — everything is generated at compile time.
- Exact-size two-pass writing (`ComputeSize` + `WriteToSpan`) so the output buffer is sized once.
- Pooled buffers (`SerializeRented`) and a zero-allocation `SerializeTo(Span)` for hot paths.
