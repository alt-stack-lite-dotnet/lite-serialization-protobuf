using Lite.Serialization.Protobuf;
using Xunit;

namespace Lite.Serialization.Protobuf.Tests;

public class GetUserClass
{
    public long Id { get; set; }
    public string Email { get; set; } = "";
    public bool IncludeDeleted { get; set; }
}

public struct GetUserStruct
{
    public long Id;
    public string Email;
    public bool IncludeDeleted;
}

public record class GetUserRecordClass(long Id, string Email, bool IncludeDeleted);

public record struct GetUserRecordStruct(long Id, string Email, bool IncludeDeleted);

public class RoundTripTests
{
    [Fact]
    public void Class_RoundTrip()
    {
        var s = LiteSerializer.For<GetUserClass>();
        var original = new GetUserClass { Id = 42, Email = "a@b.c", IncludeDeleted = true };
        var rt = s.Deserialize(s.Serialize(original));

        Assert.Equal(42, rt.Id);
        Assert.Equal("a@b.c", rt.Email);
        Assert.True(rt.IncludeDeleted);
    }

    [Fact]
    public void Struct_RoundTrip()
    {
        var s = LiteSerializer.For<GetUserStruct>();
        var original = new GetUserStruct { Id = 7, Email = "x", IncludeDeleted = false };
        var rt = s.Deserialize(s.Serialize(original));

        Assert.Equal(7, rt.Id);
        Assert.Equal("x", rt.Email);
        Assert.False(rt.IncludeDeleted);
    }

    [Fact]
    public void RecordClass_RoundTrip()
    {
        var s = LiteSerializer.For<GetUserRecordClass>();
        var original = new GetUserRecordClass(99, "rec", true);
        var rt = s.Deserialize(s.Serialize(original));
        Assert.Equal(original, rt);
    }

    [Fact]
    public void RecordStruct_RoundTrip()
    {
        var s = LiteSerializer.For<GetUserRecordStruct>();
        var original = new GetUserRecordStruct(123, "rs", false);
        var rt = s.Deserialize(s.Serialize(original));
        Assert.Equal(original, rt);
    }

    [Fact]
    public void MarshallerFor_ReturnsGrpcMarshaller()
    {
        var m = LiteSerializer.MarshallerFor<GetUserClass>();
        Assert.NotNull(m);
        Assert.NotNull(m.ContextualSerializer);
        Assert.NotNull(m.ContextualDeserializer);
    }

    [Fact]
    public void ProtoSchema_GeneratedAndContainsTypes()
    {
        var schemas = ProtoSchemaRegistry.Enumerate(typeof(GetUserClass).Assembly).ToList();
        Assert.NotEmpty(schemas);
        var combined = string.Concat(schemas.Select(s => s.Content));
        Assert.Contains("syntax = \"proto3\";", combined);
        Assert.Contains("message GetUserClass", combined);
        Assert.Contains("message GetUserStruct", combined);
        Assert.Contains("message GetUserRecordClass", combined);
        Assert.Contains("message GetUserRecordStruct", combined);
    }
}
