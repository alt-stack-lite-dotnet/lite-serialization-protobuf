using System.Linq;
using Lite.Serialization.Protobuf;
using Lite.Serialization.Protobuf.Fluent;
using Xunit;

namespace Lite.Serialization.Protobuf.Tests;

public sealed class ConfiguredMessage
{
    public long Id { get; set; }
    public string Visible { get; set; } = "";
    public string Hidden { get; set; } = "";
    public int Renamed { get; set; }
}

// Discovered by the source generator purely by implementing IProtoSerializerConfiguration<T>.
public sealed class ConfiguredMessageConfig : IProtoSerializerConfiguration<ConfiguredMessage>
{
    public void Configure(IProtoSerializerBuilder<ConfiguredMessage> b)
    {
        b.Field(x => x.Id).Tag(100);
        b.Field(x => x.Hidden).Ignore();
        b.Field(x => x.Renamed).Name("custom_field");
    }
}

// A probe whose explicit field number matches the overridden tag, used to confirm the override
// actually moved the field on the wire (not just in the schema text).
public sealed class TagProbe
{
    public const int ValueFieldNumber = 100;
    public long Value { get; set; }
}

public class ConfigOverrideTests
{
    private static string Schema()
    {
        _ = LiteSerializer.For<ConfiguredMessage>();
        var schemas = ProtoSchemaRegistry.Enumerate(typeof(ConfiguredMessage).Assembly).ToList();
        return string.Concat(schemas.Select(s => s.Content));
    }

    [Fact]
    public void TagOverride_MovesFieldOnTheWire()
    {
        var msg = new ConfiguredMessage { Id = 123 };
        byte[] bytes = LiteSerializer.For<ConfiguredMessage>().Serialize(msg);

        // Read through a type that expects the value at tag 100.
        var probe = LiteSerializer.DeserializeFrom<TagProbe>(bytes);
        Assert.Equal(123, probe.Value);
    }

    [Fact]
    public void TagOverride_ReflectedInSchema()
    {
        Assert.Contains("id = 100;", Schema());
    }

    [Fact]
    public void Ignore_DropsFieldFromWireAndSchema()
    {
        var schema = Schema();
        Assert.DoesNotContain("hidden", schema);

        var msg = new ConfiguredMessage { Id = 1, Visible = "v", Hidden = "secret" };
        var rt = LiteSerializer.DeserializeFrom<ConfiguredMessage>(LiteSerializer.For<ConfiguredMessage>().Serialize(msg));
        Assert.Equal("v", rt.Visible);
        Assert.Equal("", rt.Hidden); // never serialized
    }

    [Fact]
    public void NameOverride_ReflectedInSchema()
    {
        var schema = Schema();
        Assert.Contains("custom_field", schema);
        Assert.DoesNotContain("renamed", schema);
    }

    [Fact]
    public void ConfiguredMessage_StillRoundTrips()
    {
        var msg = new ConfiguredMessage { Id = 9, Visible = "hi", Renamed = 5 };
        var rt = LiteSerializer.DeserializeFrom<ConfiguredMessage>(LiteSerializer.For<ConfiguredMessage>().Serialize(msg));
        Assert.Equal(9, rt.Id);
        Assert.Equal("hi", rt.Visible);
        Assert.Equal(5, rt.Renamed);
    }
}
