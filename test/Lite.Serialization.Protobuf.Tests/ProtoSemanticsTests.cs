using System;
using System.Collections.Generic;
using System.Linq;
using Lite.Serialization.Protobuf;
using Xunit;

namespace Lite.Serialization.Protobuf.Tests;

// proto3 wire-format semantics that any conformant protobuf serializer must honor.
public class ProtoSemanticsTests
{
    [Fact]
    public void DefaultScalars_AreOmittedFromWire()
    {
        var v = new LiteScalars(); // every field default
        byte[] bytes = LiteSerializer.For<LiteScalars>().Serialize(v);
        Assert.Empty(bytes);
    }

    [Fact]
    public void NonDefaultZeroAdjacentValues_AreWritten()
    {
        // Smallest non-default values still produce wire bytes.
        var v = new LiteScalars { I32 = 1 };
        byte[] bytes = LiteSerializer.For<LiteScalars>().Serialize(v);
        Assert.NotEmpty(bytes);

        var rt = LiteSerializer.DeserializeFrom<LiteScalars>(bytes);
        Assert.Equal(1, rt.I32);
    }

    [Fact]
    public void FieldOrder_OnTheWire_IsIrrelevant()
    {
        // Serialize two messages each carrying a single field, then concatenate them in
        // REVERSE tag order. A conformant reader must still pick up both fields.
        var onlyField5 = LiteSerializer.For<LiteScalars>().Serialize(OnlyFlag);
        var onlyField1 = LiteSerializer.For<LiteScalars>().Serialize(OnlyI32);

        byte[] reversed = onlyField5.Concat(onlyField1).ToArray(); // field 5 BEFORE field 1
        var rt = LiteSerializer.DeserializeFrom<LiteScalars>(reversed);

        Assert.Equal(-99, rt.I32);
        Assert.True(rt.Flag);
    }

    [Fact]
    public void RepeatedScalar_IsReadable_WhenPacked_AndWhenUnpacked()
    {
        // Packed (Lite's own output for repeated int32).
        var packed = new LiteComplex { Numbers = { 5, 7, 9 } };
        var rtPacked = LiteSerializer.DeserializeFrom<LiteComplex>(LiteSerializer.For<LiteComplex>().Serialize(packed));
        Assert.Equal(new[] { 5, 7, 9 }, rtPacked.Numbers.ToArray());

        // Unpacked: field 4 emitted as individual varint entries (tag 0x20 = field 4, wire type 0).
        byte[] unpacked = { 0x20, 5, 0x20, 7, 0x20, 9 };
        var rtUnpacked = LiteSerializer.DeserializeFrom<LiteComplex>(unpacked);
        Assert.Equal(new[] { 5, 7, 9 }, rtUnpacked.Numbers.ToArray());
    }

    [Fact]
    public void LastValueWins_ForDuplicateScalarField()
    {
        // proto3: a repeated-on-wire scalar field keeps the last occurrence.
        var a = LiteSerializer.For<LiteScalars>().Serialize(OnlyI32);          // I32 = -99
        var seven = new LiteScalars { I32 = 7 };
        var b = LiteSerializer.For<LiteScalars>().Serialize(seven);
        var rt = LiteSerializer.DeserializeFrom<LiteScalars>(a.Concat(b).ToArray());
        Assert.Equal(7, rt.I32);
    }

    private static readonly LiteScalars OnlyI32 = new() { I32 = -99 };
    private static readonly LiteScalars OnlyFlag = new() { Flag = true };
}
