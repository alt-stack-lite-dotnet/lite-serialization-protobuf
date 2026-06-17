using System.Buffers;
using BenchmarkDotNet.Attributes;
using Google.Protobuf;
using Lite.Serialization.Protobuf;
using Lite.Serialization.Protobuf.Benchmarks.Proto;

namespace Lite.Serialization.Protobuf.Benchmarks;

// Large payload (1000-element collections) across all three libraries.
[MemoryDiagnoser]
[Config(typeof(BenchConfig))]
public class LargeSerializeBenchmarks
{
    private static readonly BenchLargeClass _lite = BenchData.LargeLite();
    private static readonly BenchLarge _google = BenchData.LargeGoogle();
    private static readonly PnLarge _pn = BenchData.LargePn();

    [Benchmark(Baseline = true, Description = "Google.Protobuf")]
    public byte[] Google() => _google.ToByteArray();

    [Benchmark(Description = "protobuf-net")]
    public byte[] ProtobufNet() => Pn.ToBytes(_pn);

    [Benchmark(Description = "Lite (POCO class)")]
    public byte[] Lite() => LiteSerializer.Serialize<BenchLargeClass>(in _lite);
}

[MemoryDiagnoser]
[Config(typeof(BenchConfig))]
public class LargeDeserializeBenchmarks
{
    private static byte[] LiteBytes()
    {
        var v = BenchData.LargeLite();
        return LiteSerializer.Serialize<BenchLargeClass>(in v);
    }

    private static readonly byte[] _googleBytes = BenchData.LargeGoogle().ToByteArray();
    private static readonly byte[] _pnBytes = Pn.ToBytes(BenchData.LargePn());
    private static readonly byte[] _liteBytes = LiteBytes();

    [Benchmark(Baseline = true, Description = "Google.Protobuf")]
    public BenchLarge Google() => BenchLarge.Parser.ParseFrom(_googleBytes);

    [Benchmark(Description = "protobuf-net")]
    public PnLarge ProtobufNet() => Pn.From<PnLarge>(_pnBytes);

    [Benchmark(Description = "Lite (POCO class)")]
    public BenchLargeClass Lite() => LiteSerializer.Deserialize<BenchLargeClass>(new ReadOnlySequence<byte>(_liteBytes));
}
