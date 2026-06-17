using System;
using System.Linq;
using Lite.Serialization.Protobuf;
using Xunit;

namespace Lite.Serialization.Protobuf.Tests;

// Every C# scalar type Lite supports, at boundary values. Pure Lite round-trip
// (Guid/DateTime/decimal use Lite-specific encodings that are not standard-proto).
public sealed class ScalarBag
{
    public int I { get; set; }
    public long L { get; set; }
    public uint UI { get; set; }
    public ulong UL { get; set; }
    public bool B { get; set; }
    public float F { get; set; }
    public double D { get; set; }
    public string S { get; set; } = "";
    public byte[] Bytes { get; set; } = Array.Empty<byte>();
    public byte By { get; set; }
    public sbyte SB { get; set; }
    public short Sh { get; set; }
    public ushort USh { get; set; }
    public Status En { get; set; }
    public Guid G { get; set; }
    public DateTime Dt { get; set; }
    public decimal Dec { get; set; }
}

// Nullable value types — proto3 semantics: a missing field reads back as null.
public sealed class NullableBag
{
    public int? I { get; set; }
    public bool? B { get; set; }
    public long? L { get; set; }
    public Status? En { get; set; }
    public DateTime? Dt { get; set; }
}

public class ScalarMatrixTests
{
    private static ScalarBag Roundtrip(ScalarBag v) =>
        LiteSerializer.DeserializeFrom<ScalarBag>(LiteSerializer.For<ScalarBag>().Serialize(v));

    [Fact]
    public void MaxBoundaries_RoundTrip()
    {
        var g = Guid.NewGuid();
        var dt = DateTime.UtcNow;
        var v = new ScalarBag
        {
            I = int.MaxValue, L = long.MaxValue, UI = uint.MaxValue, UL = ulong.MaxValue,
            B = true, F = float.MaxValue, D = double.MaxValue, S = "Ω≈ç√∫˜µ≤≥÷ 漢字 🚀",
            Bytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray(),
            By = byte.MaxValue, SB = sbyte.MaxValue, Sh = short.MaxValue, USh = ushort.MaxValue,
            En = Status.Banned, G = g, Dt = dt, Dec = decimal.MaxValue,
        };
        var rt = Roundtrip(v);

        Assert.Equal(int.MaxValue, rt.I);
        Assert.Equal(long.MaxValue, rt.L);
        Assert.Equal(uint.MaxValue, rt.UI);
        Assert.Equal(ulong.MaxValue, rt.UL);
        Assert.True(rt.B);
        Assert.Equal(float.MaxValue, rt.F);
        Assert.Equal(double.MaxValue, rt.D);
        Assert.Equal(v.S, rt.S);
        Assert.Equal(v.Bytes, rt.Bytes);
        Assert.Equal(byte.MaxValue, rt.By);
        Assert.Equal(sbyte.MaxValue, rt.SB);
        Assert.Equal(short.MaxValue, rt.Sh);
        Assert.Equal(ushort.MaxValue, rt.USh);
        Assert.Equal(Status.Banned, rt.En);
        Assert.Equal(g, rt.G);
        Assert.Equal(dt.Ticks, rt.Dt.Ticks);
        Assert.Equal(decimal.MaxValue, rt.Dec);
    }

    [Fact]
    public void MinAndNegativeBoundaries_RoundTrip()
    {
        var v = new ScalarBag
        {
            I = int.MinValue, L = long.MinValue, UI = 0, UL = 0,
            B = false, F = float.MinValue, D = double.MinValue, S = "",
            Bytes = Array.Empty<byte>(),
            By = 0, SB = sbyte.MinValue, Sh = short.MinValue, USh = 0,
            En = Status.Active, G = Guid.Empty, Dt = default, Dec = decimal.MinValue,
        };
        var rt = Roundtrip(v);

        Assert.Equal(int.MinValue, rt.I);
        Assert.Equal(long.MinValue, rt.L);
        Assert.Equal(float.MinValue, rt.F);
        Assert.Equal(double.MinValue, rt.D);
        Assert.Equal(sbyte.MinValue, rt.SB);
        Assert.Equal(short.MinValue, rt.Sh);
        Assert.Equal(decimal.MinValue, rt.Dec);
        Assert.Equal(Guid.Empty, rt.G);
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(float.Epsilon)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void FloatSpecialValues_RoundTrip(float f)
    {
        var rt = Roundtrip(new ScalarBag { F = f, I = 1 /* force non-empty */ });
        Assert.Equal(BitConverter.SingleToInt32Bits(f), BitConverter.SingleToInt32Bits(rt.F));
    }

    [Fact]
    public void DecimalFractions_RoundTrip()
    {
        foreach (var d in new[] { 0.1m, -0.0000001m, 123456789.987654321m, 79228162514264337593543950335m })
        {
            var rt = Roundtrip(new ScalarBag { Dec = d });
            Assert.Equal(d, rt.Dec);
        }
    }

    [Fact]
    public void Nullable_NonNull_RoundTrips()
    {
        var v = new NullableBag { I = 5, B = true, L = -9, En = Status.Disabled, Dt = DateTime.UtcNow };
        var rt = LiteSerializer.DeserializeFrom<NullableBag>(LiteSerializer.For<NullableBag>().Serialize(v));
        Assert.Equal(5, rt.I);
        Assert.True(rt.B);
        Assert.Equal(-9, rt.L);
        Assert.Equal(Status.Disabled, rt.En);
        Assert.NotNull(rt.Dt);
    }

    [Fact]
    public void Nullable_AllNull_RoundTripsAsNull()
    {
        var v = new NullableBag();
        var rt = LiteSerializer.DeserializeFrom<NullableBag>(LiteSerializer.For<NullableBag>().Serialize(v));
        Assert.Null(rt.I);
        Assert.Null(rt.B);
        Assert.Null(rt.L);
        Assert.Null(rt.En);
        Assert.Null(rt.Dt);
    }
}
