using System.Collections.Generic;
using ProtoBuf;

namespace Lite.Serialization.Protobuf.Benchmarks;

// protobuf-net (Marc Gravell) contract types — the same wire format as Lite/Google, code-first via
// attributes. Field numbers match bench.proto so all three libraries produce comparable output.

[ProtoContract]
public class PnUser
{
    [ProtoMember(1)] public long Id { get; set; }
    [ProtoMember(2)] public string Name { get; set; } = "";
    [ProtoMember(3)] public string Email { get; set; } = "";
    [ProtoMember(4)] public bool IsActive { get; set; }
    [ProtoMember(5)] public int Age { get; set; }
    [ProtoMember(6)] public List<string> Tags { get; set; } = new();
}

[ProtoContract]
public class PnInner
{
    [ProtoMember(1)] public int X { get; set; }
    [ProtoMember(2)] public string Label { get; set; } = "";
}

[ProtoContract]
public class PnMedium
{
    [ProtoMember(1)] public long Id { get; set; }
    [ProtoMember(2)] public string Name { get; set; } = "";
    [ProtoMember(3)] public PnInner? Inner { get; set; }
    [ProtoMember(4)] public List<int> Numbers { get; set; } = new();
    [ProtoMember(5)] public List<string> Tags { get; set; } = new();
    [ProtoMember(6)] public Dictionary<string, int> Counts { get; set; } = new();
}

[ProtoContract]
public class PnLarge
{
    [ProtoMember(1)] public List<long> Ids { get; set; } = new();
    [ProtoMember(2)] public List<string> Names { get; set; } = new();
    [ProtoMember(3)] public List<PnInner> Items { get; set; } = new();
}
