using Lite.Serialization.Protobuf;
using Xunit;

namespace Lite.Serialization.Protobuf.Tests;

// Mimics a Grpc.Tools-generated message: explicit FieldNumber consts carry the real proto tags.
public class ExplicitTagMessage
{
    public const int IdFieldNumber = 1;
    public long Id { get; set; }

    public const int LabelFieldNumber = 2;
    public string Label { get; set; } = "";

    public const int FlagFieldNumber = 3;
    public bool Flag { get; set; }
}

public class FieldNumberTests
{
    [Fact]
    public void ExplicitFieldNumbers_UsedAsTags()
    {
        _ = LiteSerializer.For<ExplicitTagMessage>();
        var schemas = ProtoSchemaRegistry.Enumerate(typeof(ExplicitTagMessage).Assembly).ToList();
        var content = string.Concat(schemas.Select(s => s.Content));

        // Tags must be the explicit 1/2/3, not name-hash
        Assert.Contains("int64 id = 1;", content);
        Assert.Contains("string label = 2;", content);
        Assert.Contains("bool flag = 3;", content);
    }

    [Fact]
    public void ExplicitFieldNumbers_RoundTrip()
    {
        var s = LiteSerializer.For<ExplicitTagMessage>();
        var original = new ExplicitTagMessage { Id = 777, Label = "hi", Flag = true };
        var rt = s.Deserialize(s.Serialize(original));

        Assert.Equal(777, rt.Id);
        Assert.Equal("hi", rt.Label);
        Assert.True(rt.Flag);
    }
}
