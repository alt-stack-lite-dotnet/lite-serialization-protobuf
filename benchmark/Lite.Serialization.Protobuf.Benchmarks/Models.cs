using System.Collections.Generic;

namespace Lite.Serialization.Protobuf.Benchmarks;

// POCO struct — value type, zero GC alloc on instance, our lib's strength
public struct BenchUserStruct
{
    public long Id;
    public string Name;
    public string Email;
    public bool IsActive;
    public int Age;
    public List<string> Tags;
}

// POCO class — same shape but ref type, comparable to Google.Protobuf-generated class
public class BenchUserClass
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public bool IsActive { get; set; }
    public int Age { get; set; }
    public List<string> Tags { get; set; } = new();
}

// record class — primary ctor; init-only props; deserialized via ctor invocation
public record class BenchUserRecord(
    long Id,
    string Name,
    string Email,
    bool IsActive,
    int Age,
    List<string> Tags);

// record struct — primary ctor; value type; init-only props
public record struct BenchUserRecordStruct(
    long Id,
    string Name,
    string Email,
    bool IsActive,
    int Age,
    List<string> Tags);
