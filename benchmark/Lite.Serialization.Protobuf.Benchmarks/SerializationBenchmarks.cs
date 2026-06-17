using System.Buffers;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Google.Protobuf;
using Lite.Serialization.Protobuf;
using Lite.Serialization.Protobuf.Benchmarks.Proto;

namespace Lite.Serialization.Protobuf.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(BenchConfig))]
public class SerializeBenchmarks
{
    private static readonly BenchUserStruct LiteStruct = MakeStruct();
    private static readonly BenchUserClass LiteClass = MakeClass();
    private static readonly BenchUser GoogleProto = MakeGoogle();
    private static readonly PnUser ProtobufNetUser = BenchData.UserPn();

    private static readonly IProtoSerializer<BenchUserStruct> SerStruct = LiteSerializer.For<BenchUserStruct>();
    private static readonly IProtoSerializer<BenchUserClass> SerClass = LiteSerializer.For<BenchUserClass>();

    [Benchmark(Baseline = true, Description = "Google.Protobuf (IMessage class)")]
    public byte[] Google_Serialize() => GoogleProto.ToByteArray();

    [Benchmark(Description = "protobuf-net (class)")]
    public byte[] ProtobufNet_Serialize() => Pn.ToBytes(ProtobufNetUser);

    [Benchmark(Description = "Lite (POCO class)")]
    public byte[] LiteClass_Serialize() => SerClass.Serialize(LiteClass);

    [Benchmark(Description = "Lite (POCO struct)")]
    public byte[] LiteStruct_Serialize() => SerStruct.Serialize(LiteStruct);

    [Benchmark(Description = "Lite struct → IBufferWriter (interface dispatch)")]
    public int LiteStruct_SerializeToWriter()
    {
        var w = new ArrayBufferWriter<byte>(64);
        SerStruct.WriteTo(LiteStruct, w);
        return w.WrittenCount;
    }

    [Benchmark(Description = "Lite struct → static (intercepted, no dispatch)")]
    public int LiteStruct_SerializeStatic()
    {
        var w = new ArrayBufferWriter<byte>(64);
        LiteSerializer.SerializeTo<BenchUserStruct>(in LiteStruct, w);
        return w.WrittenCount;
    }

    [Benchmark(Description = "Lite class → static (intercepted, no dispatch)")]
    public int LiteClass_SerializeStatic()
    {
        var w = new ArrayBufferWriter<byte>(64);
        LiteSerializer.SerializeTo<BenchUserClass>(in LiteClass, w);
        return w.WrittenCount;
    }

    // Apples-to-apples vs Google.ToByteArray() — exact-size byte[] allocation, no ArrayBufferWriter
    [Benchmark(Description = "Lite struct → byte[] (intercepted, exact-size)")]
    public byte[] LiteStruct_SerializeByteArray() => LiteSerializer.For<BenchUserStruct>().Serialize(LiteStruct);

    [Benchmark(Description = "Lite class → byte[] (intercepted, exact-size)")]
    public byte[] LiteClass_SerializeByteArray() => LiteSerializer.For<BenchUserClass>().Serialize(LiteClass);

    // Zero-alloc: caller provides buffer (reused across calls)
    private readonly byte[] _reusedBuf = new byte[256];

    [Benchmark(Description = "Lite struct → SerializeTo (zero alloc)")]
    public int LiteStruct_SerializeTo() => LiteSerializer.SerializeTo<BenchUserStruct>(in LiteStruct, _reusedBuf);

    [Benchmark(Description = "Lite class → SerializeTo (zero alloc)")]
    public int LiteClass_SerializeTo() => LiteSerializer.SerializeTo<BenchUserClass>(in LiteClass, _reusedBuf);

    // Pool-backed rent: zero heap alloc per call (after pool warmup)
    [Benchmark(Description = "Lite struct → SerializeRented (MemoryPool)")]
    public int LiteStruct_SerializeRented()
    {
        using var rented = LiteSerializer.SerializeRented<BenchUserStruct>(in LiteStruct);
        return rented.Length;
    }

    [Benchmark(Description = "Lite class → SerializeRented (MemoryPool)")]
    public int LiteClass_SerializeRented()
    {
        using var rented = LiteSerializer.SerializeRented<BenchUserClass>(in LiteClass);
        return rented.Length;
    }

    private static BenchUserStruct MakeStruct() => new()
    {
        Id = 42, Name = "alice", Email = "alice@example.com",
        IsActive = true, Age = 30,
        Tags = new() { "admin", "developer", "remote" }
    };

    private static BenchUserClass MakeClass() => new()
    {
        Id = 42, Name = "alice", Email = "alice@example.com",
        IsActive = true, Age = 30,
        Tags = new() { "admin", "developer", "remote" }
    };

    private static BenchUser MakeGoogle()
    {
        var u = new BenchUser
        {
            Id = 42, Name = "alice", Email = "alice@example.com",
            IsActive = true, Age = 30,
        };
        u.Tags.Add("admin");
        u.Tags.Add("developer");
        u.Tags.Add("remote");
        return u;
    }
}

[MemoryDiagnoser]
[Config(typeof(BenchConfig))]
public class DeserializeBenchmarks
{
    private static readonly IProtoSerializer<BenchUserStruct> SerStruct = LiteSerializer.For<BenchUserStruct>();
    private static readonly IProtoSerializer<BenchUserClass> SerClass = LiteSerializer.For<BenchUserClass>();

    private static readonly byte[] LiteStructBytes = SerStruct.Serialize(SerializeBenchmarks_GetStruct());
    private static readonly byte[] LiteClassBytes = SerClass.Serialize(SerializeBenchmarks_GetClass());
    private static readonly byte[] GoogleBytes = SerializeBenchmarks_GetGoogle().ToByteArray();
    private static readonly byte[] PnBytes = Pn.ToBytes(BenchData.UserPn());

    [Benchmark(Baseline = true, Description = "Google.Protobuf (IMessage class)")]
    public BenchUser Google_Deserialize() => BenchUser.Parser.ParseFrom(GoogleBytes);

    [Benchmark(Description = "protobuf-net (class)")]
    public PnUser ProtobufNet_Deserialize() => Pn.From<PnUser>(PnBytes);

    [Benchmark(Description = "Lite (POCO class)")]
    public BenchUserClass LiteClass_Deserialize() => SerClass.Deserialize(LiteClassBytes);

    [Benchmark(Description = "Lite (POCO struct)")]
    public BenchUserStruct LiteStruct_Deserialize() => SerStruct.Deserialize(LiteStructBytes);

    [Benchmark(Description = "Lite struct ← static (intercepted)")]
    public BenchUserStruct LiteStruct_DeserializeStatic() =>
        LiteSerializer.DeserializeFrom<BenchUserStruct>(new ReadOnlySequence<byte>(LiteStructBytes));

    [Benchmark(Description = "Lite class ← static (intercepted)")]
    public BenchUserClass LiteClass_DeserializeStatic() =>
        LiteSerializer.DeserializeFrom<BenchUserClass>(new ReadOnlySequence<byte>(LiteClassBytes));

    // Record class (primary ctor → ctor-mode deserialize)
    private static readonly BenchUserRecord _liteRecord = new(42, "alice", "alice@example.com", true, 30,
        new List<string> { "admin", "developer", "remote" });
    private static readonly byte[] LiteRecordBytes = LiteSerializer.For<BenchUserRecord>().Serialize(_liteRecord);

    [Benchmark(Description = "Lite record class ← static (intercepted)")]
    public BenchUserRecord LiteRecord_DeserializeStatic() =>
        LiteSerializer.DeserializeFrom<BenchUserRecord>(new ReadOnlySequence<byte>(LiteRecordBytes));

    // Record struct (primary ctor, value type → ctor-mode deserialize, returned by value)
    private static readonly BenchUserRecordStruct _liteRecordStruct = new(42, "alice", "alice@example.com", true, 30,
        new List<string> { "admin", "developer", "remote" });
    private static readonly byte[] LiteRecordStructBytes = LiteSerializer.For<BenchUserRecordStruct>().Serialize(_liteRecordStruct);

    [Benchmark(Description = "Lite record struct ← static (intercepted)")]
    public BenchUserRecordStruct LiteRecordStruct_DeserializeStatic() =>
        LiteSerializer.DeserializeFrom<BenchUserRecordStruct>(new ReadOnlySequence<byte>(LiteRecordStructBytes));

    private static BenchUserStruct SerializeBenchmarks_GetStruct() => new()
    {
        Id = 42, Name = "alice", Email = "alice@example.com",
        IsActive = true, Age = 30,
        Tags = new() { "admin", "developer", "remote" }
    };

    private static BenchUserClass SerializeBenchmarks_GetClass() => new()
    {
        Id = 42, Name = "alice", Email = "alice@example.com",
        IsActive = true, Age = 30,
        Tags = new() { "admin", "developer", "remote" }
    };

    private static BenchUser SerializeBenchmarks_GetGoogle()
    {
        var u = new BenchUser
        {
            Id = 42, Name = "alice", Email = "alice@example.com",
            IsActive = true, Age = 30,
        };
        u.Tags.Add("admin");
        u.Tags.Add("developer");
        u.Tags.Add("remote");
        return u;
    }
}
