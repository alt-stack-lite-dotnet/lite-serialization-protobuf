using BenchmarkDotNet.Running;

namespace Lite.Serialization.Protobuf.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        // `dotnet run -c Release -- sizes` prints the wire-size table instead of running benchmarks.
        if (args.Length == 1 && args[0] == "sizes")
        {
            SizeReport.Print();
            return;
        }

        // `dotnet run -c Release -- quick` runs the lightweight in-process micro-bench (ns/op + B/op).
        if (args.Length == 1 && args[0] == "quick")
        {
            ManualBench.Run();
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
