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

| Scenario | Google (IMessage) | protobuf-net | Lite | Lite vs Google |
|---|--:|--:|--:|:--:|
| Small serialize → byte[] | 223 ns / 152 B | 437 ns / 432 B | **182 ns / 88 B** | **1.2× · 0.58× mem** |
| Small serialize (`SerializeTo`) | — | — | **114 ns / 0 B** | **zero alloc** |
| Small deserialize | 296 ns / 552 B | 538 ns / 464 B | **202 ns / 376 B** | **1.5×** |
| Medium serialize | 526 ns / 208 B | 955 ns / 480 B | **306 ns / 88 B** | **1.7× · 0.42× mem** |
| Medium deserialize | 854 ns / 1176 B | 1982 ns / 872 B | **631 ns / 904 B** | **1.4×** |
| Large serialize | 99.6 µs / 26.6 KB | 148 µs / 92 KB | **87.5 µs / 26.6 KB** | **1.14× · min mem** |
| Large deserialize | 159 µs / 170 KB | 213 µs / 136 KB | **82 µs / 162 KB** | **1.9×** |

### Type kinds (Small serialize, vs IMessage)

Google.Protobuf can only generate classes; Lite serializes any POCO kind, with no message-object
allocation on the hot path.

| Kind | ns/op | B/op |
|---|--:|--:|
| Google.Protobuf (class, IMessage) | 172 | 152 |
| Lite `class` | 223 | 88 |
| Lite `struct` | 172 | 88 |
| Lite `record class` | 306 | 88 |
| Lite `record struct` | 152 | 88 |
| Lite `readonly record struct` | 152 | 88 |

### Reading the numbers

- Lite leads on **every** scenario — serialize and deserialize, small to large — and offers a true
  **zero-allocation** path (`SerializeTo` into a caller buffer → 0 B/op).
- `Serialize → byte[]`'s allocation IS the returned array (payload + 24 B .NET array header) — not a
  temp buffer. It's still leaner than Google.Protobuf (152 B) and protobuf-net (432 B) for the same
  result. For zero allocation, pass your own buffer to `SerializeTo` — `stackalloc` for small payloads,
  `ArrayPool` for large.
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
