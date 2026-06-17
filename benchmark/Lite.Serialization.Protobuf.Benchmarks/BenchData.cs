using System.Collections.Generic;
using System.IO;
using Lite.Serialization.Protobuf.Benchmarks.Proto;

namespace Lite.Serialization.Protobuf.Benchmarks;

// protobuf-net byte[] helpers (it is stream-oriented).
internal static class Pn
{
    public static byte[] ToBytes<T>(T v)
    {
        using var ms = new MemoryStream();
        ProtoBuf.Serializer.Serialize(ms, v);
        return ms.ToArray();
    }

    public static T From<T>(byte[] b)
    {
        using var ms = new MemoryStream(b);
        return ProtoBuf.Serializer.Deserialize<T>(ms);
    }
}

// Shared sample factories so every benchmark and the size report use identical data.
internal static class BenchData
{
    public const int LargeN = 1000;

    private static readonly string[] SmallTags = { "admin", "developer", "remote" };

    // ── Small (BenchUser), all five C# kinds + the two competitors ──
    public static BenchUserClass UserClass() => new()
    { Id = 42, Name = "alice", Email = "alice@example.com", IsActive = true, Age = 30, Tags = new(SmallTags) };

    public static BenchUserStruct UserStruct() => new()
    { Id = 42, Name = "alice", Email = "alice@example.com", IsActive = true, Age = 30, Tags = new(SmallTags) };

    public static BenchUserRecord UserRecord() =>
        new(42, "alice", "alice@example.com", true, 30, new(SmallTags));

    public static BenchUserRecordStruct UserRecordStruct() =>
        new(42, "alice", "alice@example.com", true, 30, new(SmallTags));

    public static BenchUserReadonlyRecordStruct UserReadonlyRecordStruct() =>
        new(42, "alice", "alice@example.com", true, 30, new(SmallTags));

    public static PnUser UserPn() => new()
    { Id = 42, Name = "alice", Email = "alice@example.com", IsActive = true, Age = 30, Tags = new(SmallTags) };

    public static BenchUser UserGoogle()
    {
        var u = new BenchUser { Id = 42, Name = "alice", Email = "alice@example.com", IsActive = true, Age = 30 };
        u.Tags.Add(SmallTags);
        return u;
    }

    // ── Medium: nested + repeated + map ──
    public static BenchMediumClass MediumLite() => new()
    {
        Id = 42, Name = "medium",
        Inner = new BenchInnerClass { X = 7, Label = "deep" },
        Numbers = { 1, 2, 3, 4, 5, 100, 1_000_000 },
        Tags = { "a", "bb", "ccc" },
        Counts = { ["x"] = 1, ["y"] = 2, ["z"] = 3 },
    };

    public static PnMedium MediumPn() => new()
    {
        Id = 42, Name = "medium",
        Inner = new PnInner { X = 7, Label = "deep" },
        Numbers = { 1, 2, 3, 4, 5, 100, 1_000_000 },
        Tags = { "a", "bb", "ccc" },
        Counts = { ["x"] = 1, ["y"] = 2, ["z"] = 3 },
    };

    public static BenchMedium MediumGoogle()
    {
        var m = new BenchMedium { Id = 42, Name = "medium", Inner = new BenchInner { X = 7, Label = "deep" } };
        m.Numbers.AddRange(new[] { 1, 2, 3, 4, 5, 100, 1_000_000 });
        m.Tags.AddRange(new[] { "a", "bb", "ccc" });
        m.Counts.Add("x", 1); m.Counts.Add("y", 2); m.Counts.Add("z", 3);
        return m;
    }

    // ── Large: big collections ──
    public static BenchLargeClass LargeLite()
    {
        var x = new BenchLargeClass();
        for (int i = 0; i < LargeN; i++)
        {
            x.Ids.Add(i);
            x.Names.Add("name-" + i);
            x.Items.Add(new BenchInnerClass { X = i, Label = "item-" + i });
        }
        return x;
    }

    public static PnLarge LargePn()
    {
        var x = new PnLarge();
        for (int i = 0; i < LargeN; i++)
        {
            x.Ids.Add(i);
            x.Names.Add("name-" + i);
            x.Items.Add(new PnInner { X = i, Label = "item-" + i });
        }
        return x;
    }

    public static BenchLarge LargeGoogle()
    {
        var x = new BenchLarge();
        for (int i = 0; i < LargeN; i++)
        {
            x.Ids.Add(i);
            x.Names.Add("name-" + i);
            x.Items.Add(new BenchInner { X = i, Label = "item-" + i });
        }
        return x;
    }
}
