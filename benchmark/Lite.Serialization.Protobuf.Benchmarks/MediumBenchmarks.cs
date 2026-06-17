using System.Buffers;
using BenchmarkDotNet.Attributes;
using Google.Protobuf;
using Lite.Serialization.Protobuf;
using Lite.Serialization.Protobuf.Benchmarks.Proto;

namespace Lite.Serialization.Protobuf.Benchmarks;

// Medium payload (nested message + repeated + map) across all three libraries.
[MemoryDiagnoser]
[Config(typeof(BenchConfig))]
public class MediumSerializeBenchmarks
{
    private static readonly BenchMediumClass _lite = BenchData.MediumLite();
    private static readonly BenchMedium _google = BenchData.MediumGoogle();
    private static readonly PnMedium _pn = BenchData.MediumPn();

    [Benchmark(Baseline = true, Description = "Google.Protobuf")]
    public byte[] Google() => _google.ToByteArray();

    [Benchmark(Description = "protobuf-net")]
    public byte[] ProtobufNet() => Pn.ToBytes(_pn);

    [Benchmark(Description = "Lite (POCO class)")]
    public byte[] Lite() => LiteSerializer.Serialize<BenchMediumClass>(in _lite);
}

[MemoryDiagnoser]
[Config(typeof(BenchConfig))]
public class MediumDeserializeBenchmarks
{
    private static byte[] LiteBytes()
    {
        var v = BenchData.MediumLite();
        return LiteSerializer.Serialize<BenchMediumClass>(in v);
    }

    private static readonly byte[] _googleBytes = BenchData.MediumGoogle().ToByteArray();
    private static readonly byte[] _pnBytes = Pn.ToBytes(BenchData.MediumPn());
    private static readonly byte[] _liteBytes = LiteBytes();

    [Benchmark(Baseline = true, Description = "Google.Protobuf")]
    public BenchMedium Google() => BenchMedium.Parser.ParseFrom(_googleBytes);

    [Benchmark(Description = "protobuf-net")]
    public PnMedium ProtobufNet() => Pn.From<PnMedium>(_pnBytes);

    [Benchmark(Description = "Lite (POCO class)")]
    public BenchMediumClass Lite() => LiteSerializer.Deserialize<BenchMediumClass>(new ReadOnlySequence<byte>(_liteBytes));
}
