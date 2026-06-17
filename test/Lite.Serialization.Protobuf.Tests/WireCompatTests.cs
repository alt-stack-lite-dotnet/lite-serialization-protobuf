using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using Lite.Serialization.Protobuf;
using Lite.Serialization.Protobuf.Tests.Proto;
using Xunit;

namespace Lite.Serialization.Protobuf.Tests;

// ── Lite POCOs that mirror wirecompat.proto by field number (Grpc.Tools const convention) ──
// Property names are intentionally different from the proto field names: only the *tags* matter
// on the wire, and these XxxFieldNumber consts pin the tags to the proto's 1..N.

public enum LiteColor { Unspecified = 0, Red = 1, Green = 2, Blue = 3 }

public sealed class LiteInner
{
    public const int XFieldNumber = 1;
    public int X { get; set; }

    public const int LabelFieldNumber = 2;
    public string Label { get; set; } = "";
}

public sealed class LiteScalars
{
    public const int I32FieldNumber = 1;
    public int I32 { get; set; }

    public const int I64FieldNumber = 2;
    public long I64 { get; set; }

    public const int U32FieldNumber = 3;
    public uint U32 { get; set; }

    public const int U64FieldNumber = 4;
    public ulong U64 { get; set; }

    public const int FlagFieldNumber = 5;
    public bool Flag { get; set; }

    public const int FFieldNumber = 6;
    public float F { get; set; }

    public const int DFieldNumber = 7;
    public double D { get; set; }

    public const int TextFieldNumber = 8;
    public string Text { get; set; } = "";

    public const int BlobFieldNumber = 9;
    public byte[] Blob { get; set; } = Array.Empty<byte>();

    public const int ColorFieldNumber = 10;
    public LiteColor Color { get; set; }
}

public sealed class LiteComplex
{
    public const int IdFieldNumber = 1;
    public long Id { get; set; }

    public const int NameFieldNumber = 2;
    public string Name { get; set; } = "";

    public const int InnerFieldNumber = 3;
    public LiteInner? Inner { get; set; }

    public const int NumbersFieldNumber = 4;
    public List<int> Numbers { get; set; } = new();

    public const int TagsFieldNumber = 5;
    public List<string> Tags { get; set; } = new();

    public const int ItemsFieldNumber = 6;
    public List<LiteInner> Items { get; set; } = new();

    public const int CountsFieldNumber = 7;
    public Dictionary<string, int> Counts { get; set; } = new();

    public const int ColorFieldNumber = 8;
    public LiteColor Color { get; set; }
}

public class WireCompatTests
{
    // ───────────────────────── Lite bytes → Google parser ─────────────────────────

    [Fact]
    public void LiteScalars_BytesAreParsedBy_Google()
    {
        var lite = new LiteScalars
        {
            I32 = -123_456,         // negative => 10-byte varint, must match Google int32
            I64 = 9_000_000_000_000,
            U32 = 4_000_000_000,
            U64 = 18_000_000_000_000_000_000,
            Flag = true,
            F = 3.14159f,
            D = 2.718281828459045,
            Text = "héllo, мир 🌍",  // multi-byte UTF-8
            Blob = new byte[] { 0, 1, 2, 250, 255 },
            Color = LiteColor.Blue,
        };

        byte[] bytes = LiteSerializer.For<LiteScalars>().Serialize(lite);
        var g = WireScalars.Parser.ParseFrom(bytes);

        Assert.Equal(-123_456, g.I32);
        Assert.Equal(9_000_000_000_000, g.I64);
        Assert.Equal(4_000_000_000u, g.U32);
        Assert.Equal(18_000_000_000_000_000_000ul, g.U64);
        Assert.True(g.Flag);
        Assert.Equal(3.14159f, g.F);
        Assert.Equal(2.718281828459045, g.D);
        Assert.Equal("héllo, мир 🌍", g.Text);
        Assert.Equal(new byte[] { 0, 1, 2, 250, 255 }, g.Blob.ToByteArray());
        Assert.Equal(3, (int)g.Color);
    }

    [Fact]
    public void GoogleScalars_BytesAreParsedBy_Lite()
    {
        var g = new WireScalars
        {
            I32 = -7,
            I64 = -42_000_000_000,
            U32 = 65_535,
            U64 = 1_000_000_000_000,
            Flag = true,
            F = -1.5f,
            D = 123.456,
            Text = "round-trip",
            Blob = ByteString.CopyFrom(9, 8, 7),
            Color = WireColor.WireGreen,
        };

        byte[] bytes = g.ToByteArray();
        var lite = LiteSerializer.DeserializeFrom<LiteScalars>(bytes);

        Assert.Equal(-7, lite.I32);
        Assert.Equal(-42_000_000_000, lite.I64);
        Assert.Equal(65_535u, lite.U32);
        Assert.Equal(1_000_000_000_000ul, lite.U64);
        Assert.True(lite.Flag);
        Assert.Equal(-1.5f, lite.F);
        Assert.Equal(123.456, lite.D);
        Assert.Equal("round-trip", lite.Text);
        Assert.Equal(new byte[] { 9, 8, 7 }, lite.Blob);
        Assert.Equal(LiteColor.Green, lite.Color);
    }

    [Fact]
    public void Scalars_WireSize_MatchesGoogle()
    {
        var lite = new LiteScalars
        {
            I32 = 12345, I64 = 678901234, U32 = 42, U64 = 99,
            Flag = true, F = 1.25f, D = 9.5, Text = "size-check",
            Blob = new byte[] { 1, 2, 3, 4 }, Color = LiteColor.Red,
        };
        var g = new WireScalars
        {
            I32 = 12345, I64 = 678901234, U32 = 42, U64 = 99,
            Flag = true, F = 1.25f, D = 9.5, Text = "size-check",
            Blob = ByteString.CopyFrom(1, 2, 3, 4), Color = WireColor.WireRed,
        };

        byte[] liteBytes = LiteSerializer.For<LiteScalars>().Serialize(lite);
        Assert.Equal(g.CalculateSize(), liteBytes.Length);
        Assert.Equal(g.CalculateSize(), LiteSerializer.ComputeSize<LiteScalars>(in lite));
    }

    // ───────────────────────── Complex: nested / repeated / map / enum ─────────────────────────

    [Fact]
    public void LiteComplex_BytesAreParsedBy_Google()
    {
        var lite = new LiteComplex
        {
            Id = 777,
            Name = "complex",
            Inner = new LiteInner { X = 5, Label = "deep" },
            Numbers = { 1, 2, 3, 1_000_000 },
            Tags = { "a", "bb", "ccc" },
            Items = { new LiteInner { X = 1, Label = "one" }, new LiteInner { X = 2, Label = "two" } },
            Counts = { ["x"] = 10, ["y"] = 20 },
            Color = LiteColor.Blue,
        };

        byte[] bytes = LiteSerializer.For<LiteComplex>().Serialize(lite);
        var g = WireComplex.Parser.ParseFrom(bytes);

        Assert.Equal(777, g.Id);
        Assert.Equal("complex", g.Name);
        Assert.NotNull(g.Inner);
        Assert.Equal(5, g.Inner.X);
        Assert.Equal("deep", g.Inner.Label);
        Assert.Equal(new[] { 1, 2, 3, 1_000_000 }, g.Numbers.ToArray());
        Assert.Equal(new[] { "a", "bb", "ccc" }, g.Tags.ToArray());
        Assert.Equal(2, g.Items.Count);
        Assert.Equal("two", g.Items[1].Label);
        Assert.Equal(10, g.Counts["x"]);
        Assert.Equal(20, g.Counts["y"]);
        Assert.Equal(3, (int)g.Color);
    }

    [Fact]
    public void GoogleComplex_BytesAreParsedBy_Lite()
    {
        var g = new WireComplex
        {
            Id = 555,
            Name = "from-google",
            Inner = new WireInner { X = 99, Label = "g-inner" },
            Color = WireColor.WireGreen,
        };
        g.Numbers.AddRange(new[] { 7, 8, 9 });
        g.Tags.AddRange(new[] { "p", "qq" });
        g.Items.Add(new WireInner { X = 1, Label = "i1" });
        g.Counts.Add("k", 42);

        byte[] bytes = g.ToByteArray();
        var lite = LiteSerializer.DeserializeFrom<LiteComplex>(bytes);

        Assert.Equal(555, lite.Id);
        Assert.Equal("from-google", lite.Name);
        Assert.NotNull(lite.Inner);
        Assert.Equal(99, lite.Inner!.X);
        Assert.Equal("g-inner", lite.Inner.Label);
        Assert.Equal(new[] { 7, 8, 9 }, lite.Numbers.ToArray());
        Assert.Equal(new[] { "p", "qq" }, lite.Tags.ToArray());
        Assert.Single(lite.Items);
        Assert.Equal("i1", lite.Items[0].Label);
        Assert.Equal(42, lite.Counts["k"]);
        Assert.Equal(LiteColor.Green, lite.Color);
    }

    [Fact]
    public void Complex_WireSize_MatchesGoogle()
    {
        var lite = new LiteComplex
        {
            Id = 1, Name = "n",
            Inner = new LiteInner { X = 2, Label = "L" },
            Numbers = { 1, 2, 3 }, Tags = { "t1", "t2" },
            Items = { new LiteInner { X = 9, Label = "z" } },
            Counts = { ["c"] = 5 }, Color = LiteColor.Blue,
        };
        var g = new WireComplex
        {
            Id = 1, Name = "n", Inner = new WireInner { X = 2, Label = "L" }, Color = WireColor.WireBlue,
        };
        g.Numbers.AddRange(new[] { 1, 2, 3 });
        g.Tags.AddRange(new[] { "t1", "t2" });
        g.Items.Add(new WireInner { X = 9, Label = "z" });
        g.Counts.Add("c", 5);

        byte[] liteBytes = LiteSerializer.For<LiteComplex>().Serialize(lite);
        Assert.Equal(g.CalculateSize(), liteBytes.Length);
    }

    // ───────────────────────── proto3 default omission ─────────────────────────

    [Fact]
    public void DefaultValues_ProduceEmptyWire_LikeProto3()
    {
        var lite = new LiteScalars(); // all defaults
        byte[] bytes = LiteSerializer.For<LiteScalars>().Serialize(lite);

        Assert.Empty(bytes); // proto3: default scalars are not written
        var g = WireScalars.Parser.ParseFrom(bytes);
        Assert.Equal(0, g.I32);
        Assert.False(g.Flag);
        Assert.Equal("", g.Text);
    }
}
