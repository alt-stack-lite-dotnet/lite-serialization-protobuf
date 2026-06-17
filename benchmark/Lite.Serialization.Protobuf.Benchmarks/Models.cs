using System.Collections.Generic;

namespace Lite.Serialization.Protobuf.Benchmarks;

// ═══════════════════════ Small (BenchUser) — the same shape in all five C# kinds ═══════════════════════
// All carry explicit field numbers (const XxxFieldNumber, the Grpc.Tools convention) matching bench.proto,
// so Lite's wire output is byte-identical to Google.Protobuf / protobuf-net — a fair comparison.

// POCO struct — value type, zero GC alloc on the instance.
public struct BenchUserStruct
{
    public const int IdFieldNumber = 1;
    public long Id;

    public const int NameFieldNumber = 2;
    public string Name;

    public const int EmailFieldNumber = 3;
    public string Email;

    public const int IsActiveFieldNumber = 4;
    public bool IsActive;

    public const int AgeFieldNumber = 5;
    public int Age;

    public const int TagsFieldNumber = 6;
    public List<string> Tags;
}

// POCO class — ref type, comparable to a Google.Protobuf-generated class.
public class BenchUserClass
{
    public const int IdFieldNumber = 1;
    public long Id { get; set; }

    public const int NameFieldNumber = 2;
    public string Name { get; set; } = "";

    public const int EmailFieldNumber = 3;
    public string Email { get; set; } = "";

    public const int IsActiveFieldNumber = 4;
    public bool IsActive { get; set; }

    public const int AgeFieldNumber = 5;
    public int Age { get; set; }

    public const int TagsFieldNumber = 6;
    public List<string> Tags { get; set; } = new();
}

// record class — primary ctor; deserialized via ctor invocation.
public record class BenchUserRecord(
    long Id, string Name, string Email, bool IsActive, int Age, List<string> Tags)
{
    public const int IdFieldNumber = 1;
    public const int NameFieldNumber = 2;
    public const int EmailFieldNumber = 3;
    public const int IsActiveFieldNumber = 4;
    public const int AgeFieldNumber = 5;
    public const int TagsFieldNumber = 6;
}

// record struct — primary ctor; value type.
public record struct BenchUserRecordStruct(
    long Id, string Name, string Email, bool IsActive, int Age, List<string> Tags)
{
    public const int IdFieldNumber = 1;
    public const int NameFieldNumber = 2;
    public const int EmailFieldNumber = 3;
    public const int IsActiveFieldNumber = 4;
    public const int AgeFieldNumber = 5;
    public const int TagsFieldNumber = 6;
}

// readonly record struct — immutable value type; the most constrained POCO shape Lite supports.
public readonly record struct BenchUserReadonlyRecordStruct(
    long Id, string Name, string Email, bool IsActive, int Age, List<string> Tags)
{
    public const int IdFieldNumber = 1;
    public const int NameFieldNumber = 2;
    public const int EmailFieldNumber = 3;
    public const int IsActiveFieldNumber = 4;
    public const int AgeFieldNumber = 5;
    public const int TagsFieldNumber = 6;
}

// ═══════════════════════ Medium — nested + repeated + map ═══════════════════════
public class BenchInnerClass
{
    public const int XFieldNumber = 1;
    public int X { get; set; }

    public const int LabelFieldNumber = 2;
    public string Label { get; set; } = "";
}

public class BenchMediumClass
{
    public const int IdFieldNumber = 1;
    public long Id { get; set; }

    public const int NameFieldNumber = 2;
    public string Name { get; set; } = "";

    public const int InnerFieldNumber = 3;
    public BenchInnerClass? Inner { get; set; }

    public const int NumbersFieldNumber = 4;
    public List<int> Numbers { get; set; } = new();

    public const int TagsFieldNumber = 5;
    public List<string> Tags { get; set; } = new();

    public const int CountsFieldNumber = 6;
    public Dictionary<string, int> Counts { get; set; } = new();
}

// ═══════════════════════ Large — big collections ═══════════════════════
public class BenchLargeClass
{
    public const int IdsFieldNumber = 1;
    public List<long> Ids { get; set; } = new();

    public const int NamesFieldNumber = 2;
    public List<string> Names { get; set; } = new();

    public const int ItemsFieldNumber = 3;
    public List<BenchInnerClass> Items { get; set; } = new();
}
