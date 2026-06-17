using System;
using System.Buffers;
using Lite.Serialization.Protobuf;
using Xunit;

namespace Lite.Serialization.Protobuf.Tests;

// Exercises every public entry point on LiteSerializer and proves they agree with each other:
// ComputeSize == bytes written, all Serialize overloads produce identical bytes, all Deserialize
// overloads produce identical values.
public class ApiSurfaceTests
{
    private static LiteScalars Sample() => new()
    {
        I32 = -42, I64 = 1_000_000_000_000, U32 = 7, U64 = 9,
        Flag = true, F = 1.5f, D = 2.25, Text = "api-surface",
        Blob = new byte[] { 1, 2, 3, 4, 5 }, Color = LiteColor.Green,
    };

    [Fact]
    public void ComputeSize_EqualsActualLength()
    {
        var v = Sample();
        int computed = LiteSerializer.ComputeSize<LiteScalars>(in v);
        byte[] bytes = LiteSerializer.Serialize<LiteScalars>(in v);
        Assert.Equal(bytes.Length, computed);
    }

    [Fact]
    public void AllSerializePaths_ProduceIdenticalBytes()
    {
        var v = Sample();

        byte[] viaByteArray = LiteSerializer.Serialize<LiteScalars>(in v);

        var bw = new ArrayBufferWriter<byte>();
        LiteSerializer.Serialize<LiteScalars>(in v, bw);
        byte[] viaWriter = bw.WrittenSpan.ToArray();

        var dst = new byte[LiteSerializer.ComputeSize<LiteScalars>(in v)];
        int wrote = LiteSerializer.SerializeTo<LiteScalars>(in v, dst);
        byte[] viaSpan = dst.AsSpan(0, wrote).ToArray();

        using var rented = LiteSerializer.SerializeRented<LiteScalars>(in v);
        byte[] viaRented = rented.Span.ToArray();

        Assert.Equal(viaByteArray, viaWriter);
        Assert.Equal(viaByteArray, viaSpan);
        Assert.Equal(viaByteArray, viaRented);
        Assert.Equal(viaByteArray.Length, rented.Length);
        Assert.Equal(viaByteArray.Length, wrote);
    }

    [Fact]
    public void AllDeserializePaths_ProduceEqualValues()
    {
        var v = Sample();
        byte[] bytes = LiteSerializer.Serialize<LiteScalars>(in v);

        var fromBytes = LiteSerializer.Deserialize<LiteScalars>(bytes);
        var fromSpan = LiteSerializer.Deserialize<LiteScalars>(bytes.AsSpan());
        var fromSeq = LiteSerializer.Deserialize<LiteScalars>(new ReadOnlySequence<byte>(bytes));

        foreach (var rt in new[] { fromBytes, fromSpan, fromSeq })
        {
            Assert.Equal(v.I32, rt.I32);
            Assert.Equal(v.I64, rt.I64);
            Assert.Equal(v.Text, rt.Text);
            Assert.Equal(v.Blob, rt.Blob);
            Assert.Equal(v.Color, rt.Color);
            Assert.Equal(v.Flag, rt.Flag);
        }
    }

    [Fact]
    public void Deserialize_FromMultiSegmentSequence_Works()
    {
        var v = Sample();
        byte[] bytes = LiteSerializer.Serialize<LiteScalars>(in v);

        // Split into two segments to exercise the non-single-segment ReadFrom path.
        int mid = bytes.Length / 2;
        var first = new MemorySegment(bytes.AsMemory(0, mid));
        var second = first.Append(bytes.AsMemory(mid));
        var seq = new ReadOnlySequence<byte>(first, 0, second, bytes.Length - mid);
        Assert.False(seq.IsSingleSegment);

        var rt = LiteSerializer.Deserialize<LiteScalars>(seq);
        Assert.Equal(v.Text, rt.Text);
        Assert.Equal(v.I64, rt.I64);
        Assert.Equal(v.Blob, rt.Blob);
    }

    [Fact]
    public void InterfaceSerializer_RoundTrips()
    {
        var s = LiteSerializer.For<LiteScalars>();
        var v = Sample();
        var rt = s.Deserialize(s.Serialize(v));
        Assert.Equal(v.Text, rt.Text);
        Assert.Equal(v.U64, rt.U64);
    }

    private sealed class MemorySegment : ReadOnlySequenceSegment<byte>
    {
        public MemorySegment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public MemorySegment Append(ReadOnlyMemory<byte> memory)
        {
            var seg = new MemorySegment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = seg;
            return seg;
        }
    }
}
