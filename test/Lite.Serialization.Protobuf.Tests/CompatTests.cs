using System.Collections.Generic;
using System.Linq;
using Lite.Serialization.Protobuf;
using Xunit;

namespace Lite.Serialization.Protobuf.Tests;

// Schema evolution: two versions of a message sharing field numbers. Protobuf's core promise is
// that adding fields is forward- and backward-compatible.
public sealed class EvolV1
{
    public const int IdFieldNumber = 1;
    public long Id { get; set; }

    public const int NameFieldNumber = 2;
    public string Name { get; set; } = "";
}

public sealed class EvolV2
{
    public const int IdFieldNumber = 1;
    public long Id { get; set; }

    public const int NameFieldNumber = 2;
    public string Name { get; set; } = "";

    public const int EmailFieldNumber = 3;
    public string Email { get; set; } = "";

    public const int ScoresFieldNumber = 4;
    public List<int> Scores { get; set; } = new();
}

public class CompatTests
{
    [Fact]
    public void Forward_NewProducer_OldConsumer_SkipsUnknownFields()
    {
        var v2 = new EvolV2 { Id = 42, Name = "ada", Email = "ada@x.io", Scores = { 10, 20, 30 } };
        byte[] bytes = LiteSerializer.For<EvolV2>().Serialize(v2);

        // Old consumer only knows fields 1 and 2; 3 and 4 must be skipped, not crash.
        var v1 = LiteSerializer.DeserializeFrom<EvolV1>(bytes);
        Assert.Equal(42, v1.Id);
        Assert.Equal("ada", v1.Name);
    }

    [Fact]
    public void Backward_OldProducer_NewConsumer_DefaultsMissingFields()
    {
        var v1 = new EvolV1 { Id = 7, Name = "bob" };
        byte[] bytes = LiteSerializer.For<EvolV1>().Serialize(v1);

        var v2 = LiteSerializer.DeserializeFrom<EvolV2>(bytes);
        Assert.Equal(7, v2.Id);
        Assert.Equal("bob", v2.Name);
        Assert.Equal("", v2.Email);       // missing → default
        Assert.Empty(v2.Scores);          // missing → empty
    }

    [Fact]
    public void RoundTripThroughOldSchema_PreservesKnownFields()
    {
        var v2 = new EvolV2 { Id = 1, Name = "keep", Email = "drop@x.io", Scores = { 1, 2 } };
        var asV1 = LiteSerializer.DeserializeFrom<EvolV1>(LiteSerializer.For<EvolV2>().Serialize(v2));
        var backToV2 = LiteSerializer.DeserializeFrom<EvolV2>(LiteSerializer.For<EvolV1>().Serialize(asV1));

        Assert.Equal(1, backToV2.Id);
        Assert.Equal("keep", backToV2.Name);
        Assert.Equal("", backToV2.Email); // lost in the v1 hop, as expected
    }
}
