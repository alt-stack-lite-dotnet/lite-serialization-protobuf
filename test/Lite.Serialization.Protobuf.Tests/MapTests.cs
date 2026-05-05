using Lite.Serialization.Protobuf;

namespace Lite.Serialization.Protobuf.Tests;

public class MapHolder
{
    public Dictionary<string, int> Counts { get; set; } = new();
    public Dictionary<long, string> NamesById { get; set; } = new();
    public Dictionary<int, Address> AddressesById { get; set; } = new();
    public IReadOnlyDictionary<string, string> Settings { get; set; } = new Dictionary<string, string>();
}

public class MapTests
{
    private static MapHolder Roundtrip(MapHolder original)
    {
        var s = LiteSerializer.For<MapHolder>();
        return s.Deserialize(s.Serialize(original));
    }

    [Fact]
    public void Map_StringInt_RoundTrip()
    {
        var rt = Roundtrip(new MapHolder { Counts = new() { ["a"] = 1, ["bb"] = 2, ["ccc"] = 3 } });
        Assert.Equal(3, rt.Counts.Count);
        Assert.Equal(1, rt.Counts["a"]);
        Assert.Equal(2, rt.Counts["bb"]);
        Assert.Equal(3, rt.Counts["ccc"]);
    }

    [Fact]
    public void Map_LongString_RoundTrip()
    {
        var rt = Roundtrip(new MapHolder { NamesById = new() { [1L] = "alice", [42L] = "bob", [9999L] = "charlie" } });
        Assert.Equal(3, rt.NamesById.Count);
        Assert.Equal("alice", rt.NamesById[1L]);
        Assert.Equal("bob", rt.NamesById[42L]);
        Assert.Equal("charlie", rt.NamesById[9999L]);
    }

    [Fact]
    public void Map_IntMessage_RoundTrip()
    {
        var rt = Roundtrip(new MapHolder
        {
            AddressesById =
            {
                [1] = new Address { Street = "1 Main", City = "NYC", Zip = 10001 },
                [2] = new Address { Street = "2 Oak", City = "LA", Zip = 90001 },
            }
        });
        Assert.Equal(2, rt.AddressesById.Count);
        Assert.Equal("1 Main", rt.AddressesById[1].Street);
        Assert.Equal("LA", rt.AddressesById[2].City);
        Assert.Equal(90001, rt.AddressesById[2].Zip);
    }

    [Fact]
    public void Map_IReadOnlyDictionary_RoundTrip()
    {
        var rt = Roundtrip(new MapHolder { Settings = new Dictionary<string, string> { ["theme"] = "dark", ["lang"] = "ru" } });
        Assert.Equal(2, rt.Settings.Count);
        Assert.Equal("dark", rt.Settings["theme"]);
        Assert.Equal("ru", rt.Settings["lang"]);
    }

    [Fact]
    public void Map_Empty_RoundTrip()
    {
        var rt = Roundtrip(new MapHolder());
        Assert.NotNull(rt.Counts); Assert.Empty(rt.Counts);
        Assert.NotNull(rt.NamesById); Assert.Empty(rt.NamesById);
    }

    [Fact]
    public void ProtoSchema_HasMapDeclaration()
    {
        _ = LiteSerializer.For<MapHolder>();
        var schemas = ProtoSchemaRegistry.Enumerate(typeof(MapHolder).Assembly).ToList();
        var content = string.Concat(schemas.Select(s => s.Content));
        Assert.Contains("map<string, int32> counts", content);
        Assert.Contains("map<int64, string> names_by_id", content);
        Assert.Contains("map<int32, Address> addresses_by_id", content);
        Assert.Contains("map<string, string> settings", content);
    }
}
