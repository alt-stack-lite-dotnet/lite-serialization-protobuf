using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;

namespace Lite.Serialization.Protobuf.Benchmarks;

// Robust statistics: multiple process launches × generous warmup × many measured iterations,
// plus allocation tracking. Override per-run on the CLI, e.g. `-- --job short` for a quick pass.
public class BenchConfig : ManualConfig
{
    public BenchConfig()
    {
        AddJob(Job.Default
            .WithLaunchCount(3)      // 3 separate processes — kills cross-run outliers
            .WithWarmupCount(8)
            .WithIterationCount(20)); // 20 measured iterations per launch
        AddDiagnoser(MemoryDiagnoser.Default); // Allocated column + GC gen counts
    }
}
