# Benchmarks

This page describes the performance goals and how to run benchmarks.

**Note:** the numbers shown here should come from BenchmarkDotNet runs on your machine/CI.

## What we optimize for

- generated binders (no reflection)
- no service resolution in the hot path
- single body read via `IBodyParser` (buffering + rewind; no exception path in hot success case with `TryParseAsync`)

## Run

```bash
dotnet run -c Release --project benchmark/Lite.Serialization.Protobuf.Benchmarks/Lite.Serialization.Protobuf.Benchmarks.csproj
```

## Results

Run the benchmarks and read the `BenchmarkDotNet` output for the numbers.

