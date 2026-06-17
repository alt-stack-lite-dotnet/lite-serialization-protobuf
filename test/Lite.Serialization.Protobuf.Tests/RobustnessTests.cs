using System;
using System.Threading;
using System.Threading.Tasks;
using Lite.Serialization.Protobuf;
using Xunit;

namespace Lite.Serialization.Protobuf.Tests;

// Defensive behavior on degenerate input. A serializer that backs a network boundary must never
// hang or corrupt process state on hostile bytes; it should round-trip empties and fault on
// clearly-corrupt framing.
public class RobustnessTests
{
    [Fact]
    public void EmptyInput_DeserializesToDefaultInstance()
    {
        var rt = LiteSerializer.Deserialize<LiteScalars>(Array.Empty<byte>());
        Assert.NotNull(rt);
        Assert.Equal(0, rt.I32);
        Assert.Equal("", rt.Text);
        Assert.Empty(rt.Blob);
        Assert.Equal(LiteColor.Unspecified, rt.Color);
    }

    [Fact]
    public async Task TruncationsAtEveryOffset_NeverHang()
    {
        var v = new LiteScalars
        {
            I64 = long.MaxValue,
            Text = "a reasonably long string value to truncate inside",
            Blob = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
            Color = LiteColor.Blue,
        };
        byte[] full = LiteSerializer.Serialize<LiteScalars>(in v);

        // Cutting at a field boundary yields a valid shorter message (no throw); cutting mid-field
        // should fault. Either way the call must RETURN — never spin.
        for (int cut = 0; cut <= full.Length; cut++)
        {
            byte[] truncated = full.AsSpan(0, cut).ToArray();
            await AssertCompletesQuickly(() => { try { LiteSerializer.Deserialize<LiteScalars>(truncated); } catch { /* faulting is fine */ } });
        }
    }

    [Fact]
    public async Task ArbitraryGarbage_NeverHangs()
    {
        var rng = new Random(20260617);
        for (int i = 0; i < 64; i++)
        {
            var buf = new byte[rng.Next(0, 64)];
            rng.NextBytes(buf);
            await AssertCompletesQuickly(() => { try { LiteSerializer.Deserialize<LiteComplex>(buf); } catch { } });
        }
    }

    [Fact]
    public async Task OverlongLengthPrefix_Faults()
    {
        // Length-delimited fields whose declared length exceeds the bytes present.
        // Tags target LiteScalars: field 8 (Text) and field 9 (Blob) are length-delimited.
        byte[][] cases =
        {
            new byte[] { 0x42, 0x10, 0x01, 0x02 },         // field 8 (Text) len=16, only 2 bytes follow
            new byte[] { 0x42, 0xFF, 0xFF, 0xFF, 0x7F },   // field 8 (Text) absurd length
            new byte[] { 0x4A, 0x05, 0x01 },               // field 9 (Blob) len=5, only 1 byte follows
        };
        foreach (var bytes in cases)
            await AssertThrowsQuickly(() => LiteSerializer.Deserialize<LiteScalars>(bytes));
    }

    [Fact]
    public void NullStringField_RoundTripsToEmpty()
    {
        var v = new LiteScalars { Text = null! };
        var rt = LiteSerializer.Deserialize<LiteScalars>(LiteSerializer.Serialize<LiteScalars>(in v));
        Assert.Equal("", rt.Text);
    }

    [Fact]
    public void NullCollection_RoundTripsToEmpty()
    {
        var v = new LiteComplex { Numbers = null!, Tags = null!, Items = null! };
        var rt = LiteSerializer.Deserialize<LiteComplex>(LiteSerializer.Serialize<LiteComplex>(in v));
        Assert.NotNull(rt.Numbers); Assert.Empty(rt.Numbers);
        Assert.NotNull(rt.Tags); Assert.Empty(rt.Tags);
        Assert.NotNull(rt.Items); Assert.Empty(rt.Items);
    }

    [Fact]
    public void NullByteArray_RoundTripsToEmpty()
    {
        var v = new LiteScalars { Blob = null! };
        var rt = LiteSerializer.Deserialize<LiteScalars>(LiteSerializer.Serialize<LiteScalars>(in v));
        Assert.NotNull(rt.Blob);
        Assert.Empty(rt.Blob);
    }

    // ── helpers: run on a worker and bound the wall-clock so a hang fails instead of blocking ──

    private static async Task AssertCompletesQuickly(Action act, int ms = 3000)
    {
        using var cts = new CancellationTokenSource();
        var task = Task.Run(act);
        if (await Task.WhenAny(task, Task.Delay(ms, cts.Token)) != task)
            Assert.Fail("deserialize did not return within timeout — possible infinite loop on malformed input");
        cts.Cancel();
        await task;
    }

    private static async Task AssertThrowsQuickly(Action act, int ms = 3000)
    {
        using var cts = new CancellationTokenSource();
        var task = Task.Run(act);
        if (await Task.WhenAny(task, Task.Delay(ms, cts.Token)) != task)
            Assert.Fail("deserialize did not return within timeout — possible infinite loop on malformed input");
        cts.Cancel();
        Assert.True(task.IsFaulted, "expected an exception on clearly-corrupt framing, but deserialize returned normally");
    }
}
