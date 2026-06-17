using System.Buffers;
using BenchmarkDotNet.Attributes;
using Google.Protobuf;
using Lite.Serialization.Protobuf;
using Lite.Serialization.Protobuf.Benchmarks.Proto;

namespace Lite.Serialization.Protobuf.Benchmarks;

// The headline of a code-first serializer: it serializes ANY POCO kind, and does so against the same
// data a Google.Protobuf IMessage carries. Baseline = Google.Protobuf (IMessage); rows = Lite POCO kinds.
[MemoryDiagnoser]
[Config(typeof(BenchConfig))]
public class TypeKindSerializeBenchmarks
{
    private static readonly BenchUser _google = BenchData.UserGoogle();
    private static readonly BenchUserClass _class = BenchData.UserClass();
    private static readonly BenchUserStruct _struct = BenchData.UserStruct();
    private static readonly BenchUserRecord _record = BenchData.UserRecord();
    private static readonly BenchUserRecordStruct _recordStruct = BenchData.UserRecordStruct();
    private static readonly BenchUserReadonlyRecordStruct _roRecordStruct = BenchData.UserReadonlyRecordStruct();

    [Benchmark(Baseline = true, Description = "Google.Protobuf (IMessage)")]
    public byte[] Google() => _google.ToByteArray();

    [Benchmark(Description = "Lite class")]
    public byte[] Class() => LiteSerializer.For<BenchUserClass>().Serialize(_class);

    [Benchmark(Description = "Lite struct")]
    public byte[] Struct() => LiteSerializer.For<BenchUserStruct>().Serialize(_struct);

    [Benchmark(Description = "Lite record class")]
    public byte[] Record() => LiteSerializer.For<BenchUserRecord>().Serialize(_record);

    [Benchmark(Description = "Lite record struct")]
    public byte[] RecordStruct() => LiteSerializer.For<BenchUserRecordStruct>().Serialize(_recordStruct);

    [Benchmark(Description = "Lite readonly record struct")]
    public byte[] ReadonlyRecordStruct() => LiteSerializer.For<BenchUserReadonlyRecordStruct>().Serialize(_roRecordStruct);
}

[MemoryDiagnoser]
[Config(typeof(BenchConfig))]
public class TypeKindDeserializeBenchmarks
{
    // All five Lite kinds carry identical field numbers, so one byte buffer round-trips into any of them;
    // the Google buffer is the IMessage baseline.
    private static readonly byte[] _googleBytes = BenchData.UserGoogle().ToByteArray();
    private static readonly byte[] _class = LiteSerializer.For<BenchUserClass>().Serialize(BenchData.UserClass());
    private static readonly byte[] _struct = LiteSerializer.For<BenchUserStruct>().Serialize(BenchData.UserStruct());
    private static readonly byte[] _record = LiteSerializer.For<BenchUserRecord>().Serialize(BenchData.UserRecord());
    private static readonly byte[] _recordStruct = LiteSerializer.For<BenchUserRecordStruct>().Serialize(BenchData.UserRecordStruct());
    private static readonly byte[] _roRecordStruct = LiteSerializer.For<BenchUserReadonlyRecordStruct>().Serialize(BenchData.UserReadonlyRecordStruct());

    [Benchmark(Baseline = true, Description = "Google.Protobuf (IMessage)")]
    public BenchUser Google() => BenchUser.Parser.ParseFrom(_googleBytes);

    [Benchmark(Description = "Lite class")]
    public BenchUserClass Class() => LiteSerializer.DeserializeFrom<BenchUserClass>(new ReadOnlySequence<byte>(_class));

    [Benchmark(Description = "Lite struct")]
    public BenchUserStruct Struct() => LiteSerializer.DeserializeFrom<BenchUserStruct>(new ReadOnlySequence<byte>(_struct));

    [Benchmark(Description = "Lite record class")]
    public BenchUserRecord Record() => LiteSerializer.DeserializeFrom<BenchUserRecord>(new ReadOnlySequence<byte>(_record));

    [Benchmark(Description = "Lite record struct")]
    public BenchUserRecordStruct RecordStruct() => LiteSerializer.DeserializeFrom<BenchUserRecordStruct>(new ReadOnlySequence<byte>(_recordStruct));

    [Benchmark(Description = "Lite readonly record struct")]
    public BenchUserReadonlyRecordStruct ReadonlyRecordStruct() => LiteSerializer.DeserializeFrom<BenchUserReadonlyRecordStruct>(new ReadOnlySequence<byte>(_roRecordStruct));
}
