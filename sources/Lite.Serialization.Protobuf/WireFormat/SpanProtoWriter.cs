using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace Lite.Serialization.Protobuf.WireFormat;

/// <summary>
/// Span-backed protobuf writer. Used internally by source-generated WriteTo when the message size
/// is computed up-front, allowing a single GetSpan/Advance per message instead of per-field.
/// All hot methods are AggressiveInlining; no virtual calls in the hot path.
/// </summary>
public ref struct SpanProtoWriter
{
    private Span<byte> _span;
    private int _written;

    public SpanProtoWriter(Span<byte> span)
    {
        _span = span;
        _written = 0;
    }

    public int Written
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _written;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteRawTag(int fieldNumber, ProtoWireType wireType) =>
        WriteRawVarint(ProtoTag.Make(fieldNumber, wireType));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteRawVarint(ulong value)
    {
        while (value >= 0x80)
        {
            _span[_written++] = (byte)(value | 0x80);
            value >>= 7;
        }
        _span[_written++] = (byte)value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteRawFixed32(uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(_span.Slice(_written), value);
        _written += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteRawFixed64(ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(_span.Slice(_written), value);
        _written += 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteRawBytes(scoped ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(_span.Slice(_written));
        _written += bytes.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt32(int fieldNumber, int value)
    {
        WriteRawVarint(ProtoTag.Make(fieldNumber, ProtoWireType.Varint));
        WriteRawVarint((ulong)(long)value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt64(int fieldNumber, long value)
    {
        WriteRawVarint(ProtoTag.Make(fieldNumber, ProtoWireType.Varint));
        WriteRawVarint((ulong)value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt32(int fieldNumber, uint value)
    {
        WriteRawVarint(ProtoTag.Make(fieldNumber, ProtoWireType.Varint));
        WriteRawVarint(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt64(int fieldNumber, ulong value)
    {
        WriteRawVarint(ProtoTag.Make(fieldNumber, ProtoWireType.Varint));
        WriteRawVarint(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBool(int fieldNumber, bool value)
    {
        WriteRawVarint(ProtoTag.Make(fieldNumber, ProtoWireType.Varint));
        _span[_written++] = value ? (byte)1 : (byte)0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDouble(int fieldNumber, double value)
    {
        WriteRawVarint(ProtoTag.Make(fieldNumber, ProtoWireType.Fixed64));
        BinaryPrimitives.WriteDoubleLittleEndian(_span.Slice(_written), value);
        _written += 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteFloat(int fieldNumber, float value)
    {
        WriteRawVarint(ProtoTag.Make(fieldNumber, ProtoWireType.Fixed32));
        BinaryPrimitives.WriteSingleLittleEndian(_span.Slice(_written), value);
        _written += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteString(int fieldNumber, string? value)
    {
        if (value is null) return;
        WriteRawVarint(ProtoTag.Make(fieldNumber, ProtoWireType.LengthDelimited));
        var byteCount = Encoding.UTF8.GetByteCount(value);
        WriteRawVarint((ulong)byteCount);
        if (byteCount > 0)
        {
            Encoding.UTF8.GetBytes(value.AsSpan(), _span.Slice(_written));
            _written += byteCount;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBytes(int fieldNumber, scoped ReadOnlySpan<byte> value)
    {
        WriteRawVarint(ProtoTag.Make(fieldNumber, ProtoWireType.LengthDelimited));
        WriteRawVarint((ulong)value.Length);
        value.CopyTo(_span.Slice(_written));
        _written += value.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteFixedLengthBytes(int fieldNumber, scoped ReadOnlySpan<byte> value) =>
        WriteBytes(fieldNumber, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WritePackedVarintHeader(int fieldNumber, int payloadLength)
    {
        WriteRawVarint(ProtoTag.Make(fieldNumber, ProtoWireType.LengthDelimited));
        WriteRawVarint((ulong)payloadLength);
    }

    public void WriteMessage<T>(int fieldNumber, T value, IProtoSerializer<T> serializer)
    {
        if (value is null) return;
        var temp = new PooledBufferWriter();
        try
        {
            serializer.WriteTo(value, temp);
            WriteRawVarint(ProtoTag.Make(fieldNumber, ProtoWireType.LengthDelimited));
            WriteRawVarint((ulong)temp.WrittenLength);
            temp.WrittenSpan.CopyTo(_span.Slice(_written));
            _written += temp.WrittenLength;
        }
        finally
        {
            temp.Dispose();
        }
    }
}
