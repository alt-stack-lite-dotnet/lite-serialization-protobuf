using System;
using System.Collections.Generic;
using Lite.Serialization.Protobuf;
using Xunit;

namespace Lite.Serialization.Protobuf.Tests;

public enum Status
{
    Active = 0,
    Disabled = 1,
    Banned = 2,
}

public class Address
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public int Zip { get; set; }
}

public class UserProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public decimal Balance { get; set; }
    public Status Status { get; set; }
    public Address? PrimaryAddress { get; set; }
    public List<string> Tags { get; set; } = new();
    public int[] Scores { get; set; } = Array.Empty<int>();
    public List<Address> AllAddresses { get; set; } = new();
}

public class AdvancedTypesTests
{
    private static UserProfile RoundTripProfile(UserProfile original)
    {
        var s = LiteSerializer.For<UserProfile>();
        return s.Deserialize(s.Serialize(original));
    }

    [Fact]
    public void Enum_RoundTrip()
    {
        var rt = RoundTripProfile(new UserProfile { Status = Status.Banned, Name = "x" });
        Assert.Equal(Status.Banned, rt.Status);
    }

    [Fact]
    public void Guid_RoundTrip()
    {
        var id = Guid.NewGuid();
        var rt = RoundTripProfile(new UserProfile { Id = id });
        Assert.Equal(id, rt.Id);
    }

    [Fact]
    public void DateTime_RoundTrip()
    {
        var dt = new DateTime(2026, 5, 5, 12, 34, 56, DateTimeKind.Utc);
        var rt = RoundTripProfile(new UserProfile { CreatedAt = dt });
        Assert.Equal(dt, rt.CreatedAt.ToUniversalTime());
    }

    [Fact]
    public void Decimal_RoundTrip()
    {
        var rt = RoundTripProfile(new UserProfile { Balance = -12345.678901234m });
        Assert.Equal(-12345.678901234m, rt.Balance);
    }

    [Fact]
    public void NestedMessage_RoundTrip()
    {
        var rt = RoundTripProfile(new UserProfile
        {
            Name = "alice",
            PrimaryAddress = new Address { Street = "1 Main", City = "NYC", Zip = 10001 }
        });
        Assert.NotNull(rt.PrimaryAddress);
        Assert.Equal("1 Main", rt.PrimaryAddress!.Street);
        Assert.Equal("NYC", rt.PrimaryAddress.City);
        Assert.Equal(10001, rt.PrimaryAddress.Zip);
    }

    [Fact]
    public void RepeatedScalar_PackedRoundTrip()
    {
        var rt = RoundTripProfile(new UserProfile { Scores = new[] { 1, 2, 3, 100, 1_000_000 } });
        Assert.Equal(new[] { 1, 2, 3, 100, 1_000_000 }, rt.Scores);
    }

    [Fact]
    public void RepeatedString_UnpackedRoundTrip()
    {
        var rt = RoundTripProfile(new UserProfile { Tags = new() { "a", "bb", "ccc" } });
        Assert.Equal(new[] { "a", "bb", "ccc" }, rt.Tags);
    }

    [Fact]
    public void RepeatedMessage_RoundTrip()
    {
        var rt = RoundTripProfile(new UserProfile
        {
            AllAddresses =
            {
                new Address { Street = "1", City = "A", Zip = 1 },
                new Address { Street = "2", City = "B", Zip = 2 }
            }
        });
        Assert.Equal(2, rt.AllAddresses.Count);
        Assert.Equal("1", rt.AllAddresses[0].Street);
        Assert.Equal("B", rt.AllAddresses[1].City);
    }

    [Fact]
    public void ProtoSchema_HasEnumAndRepeated()
    {
        // Force a For<T>() call to trigger SG for UserProfile (and transitively Address, Status)
        _ = LiteSerializer.For<UserProfile>();
        var schemas = ProtoSchemaRegistry.Enumerate(typeof(UserProfile).Assembly).ToList();
        var content = string.Concat(schemas.Select(s => s.Content));
        Assert.Contains("enum Status", content);
        Assert.Contains("repeated string tags", content);
        Assert.Contains("repeated int32 scores", content);
        Assert.Contains("repeated Address all_addresses", content);
        Assert.Contains("Address primary_address", content);
        Assert.Contains("Status status", content);
    }
}
