using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace Lite.Serialization.Protobuf.WireFormat;

public ref struct ProtoWriter
{
    private readonly IBufferWriter<byte> _writer;

    public ProtoWriter(IBufferWriter<byte> writer)
    {
        _writer = writer;
    }

    // ----- Raw primitives (no tag) -----
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteRawTag(int fieldNumber, ProtoWireType wireType) =>
        WriteRawVarint(ProtoTag.Make(fieldNumber, wireType));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteRawVarint(ulong value)
    {
        var span = _writer.GetSpan(10);
        var i = EncodeVarint(span, value);
        _writer.Advance(i);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EncodeVarint(Span<byte> span, ulong value)
    {
        var i = 0;
        while (value >= 0x80)
        {
            span[i++] = (byte)(value | 0x80);
            value >>= 7;
        }
        span[i++] = (byte)value;
        return i;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteRawFixed32(uint value)
    {
        var span = _writer.GetSpan(4);
        BinaryPrimitives.WriteUInt32LittleEndian(span, value);
        _writer.Advance(4);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteRawFixed64(ulong value)
    {
        var span = _writer.GetSpan(8);
        BinaryPrimitives.WriteUInt64LittleEndian(span, value);
        _writer.Advance(8);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteRawBytes(scoped ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return;
        var span = _writer.GetSpan(bytes.Length);
        bytes.CopyTo(span);
        _writer.Advance(bytes.Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int VarintSize(ulong value)
    {
        var n = 1;
        while (value >= 0x80) { value >>= 7; n++; }
        return n;
    }

    // ----- Tagged scalar writes — combined tag+value into single GetSpan/Advance -----
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt32(int fieldNumber, int value)
    {
        var span = _writer.GetSpan(15);
        var i = EncodeVarint(span, ProtoTag.Make(fieldNumber, ProtoWireType.Varint));
        i += EncodeVarint(span.Slice(i), (ulong)(long)value);
        _writer.Advance(i);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt64(int fieldNumber, long value)
    {
        var span = _writer.GetSpan(15);
        var i = EncodeVarint(span, ProtoTag.Make(fieldNumber, ProtoWireType.Varint));
        i += EncodeVarint(span.Slice(i), (ulong)value);
        _writer.Advance(i);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt32(int fieldNumber, uint value)
    {
        var span = _writer.GetSpan(10);
        var i = EncodeVarint(span, ProtoTag.Make(fieldNumber, ProtoWireType.Varint));
        i += EncodeVarint(span.Slice(i), value);
        _writer.Advance(i);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt64(int fieldNumber, ulong value)
    {
        var span = _writer.GetSpan(15);
        var i = EncodeVarint(span, ProtoTag.Make(fieldNumber, ProtoWireType.Varint));
        i += EncodeVarint(span.Slice(i), value);
        _writer.Advance(i);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBool(int fieldNumber, bool value)
    {
        var span = _writer.GetSpan(6);
        var i = EncodeVarint(span, ProtoTag.Make(fieldNumber, ProtoWireType.Varint));
        span[i++] = value ? (byte)1 : (byte)0;
        _writer.Advance(i);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDouble(int fieldNumber, double value)
    {
        var span = _writer.GetSpan(13);
        var i = EncodeVarint(span, ProtoTag.Make(fieldNumber, ProtoWireType.Fixed64));
        BinaryPrimitives.WriteDoubleLittleEndian(span.Slice(i), value);
        _writer.Advance(i + 8);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteFloat(int fieldNumber, float value)
    {
        var span = _writer.GetSpan(9);
        var i = EncodeVarint(span, ProtoTag.Make(fieldNumber, ProtoWireType.Fixed32));
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(i), value);
        _writer.Advance(i + 4);
    }

    public void WriteString(int fieldNumber, string? value)
    {
        if (value is null) return;
        var byteCount = Encoding.UTF8.GetByteCount(value);
        // Combined: tag (≤5) + length-varint (≤5) + body
        var span = _writer.GetSpan(10 + byteCount);
        var i = EncodeVarint(span, ProtoTag.Make(fieldNumber, ProtoWireType.LengthDelimited));
        i += EncodeVarint(span.Slice(i), (ulong)byteCount);
        if (byteCount > 0)
        {
            Encoding.UTF8.GetBytes(value.AsSpan(), span.Slice(i));
            i += byteCount;
        }
        _writer.Advance(i);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBytes(int fieldNumber, scoped ReadOnlySpan<byte> value)
    {
        var span = _writer.GetSpan(10 + value.Length);
        var i = EncodeVarint(span, ProtoTag.Make(fieldNumber, ProtoWireType.LengthDelimited));
        i += EncodeVarint(span.Slice(i), (ulong)value.Length);
        if (!value.IsEmpty)
        {
            value.CopyTo(span.Slice(i));
            i += value.Length;
        }
        _writer.Advance(i);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteFixedLengthBytes(int fieldNumber, scoped ReadOnlySpan<byte> value) =>
        WriteBytes(fieldNumber, value);

    public void WriteMessage<T>(int fieldNumber, T value, IProtoSerializer<T> serializer)
    {
        if (value is null) return;
        var temp = new PooledBufferWriter();
        try
        {
            serializer.WriteTo(value, temp);
            var len = temp.WrittenLength;
            var span = _writer.GetSpan(10 + len);
            var i = EncodeVarint(span, ProtoTag.Make(fieldNumber, ProtoWireType.LengthDelimited));
            i += EncodeVarint(span.Slice(i), (ulong)len);
            temp.WrittenSpan.CopyTo(span.Slice(i));
            _writer.Advance(i + len);
        }
        finally
        {
            temp.Dispose();
        }
    }

    // For packed repeated of varints: caller pre-computes payload length and writes tag+length, then this
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WritePackedVarintHeader(int fieldNumber, int payloadLength)
    {
        var span = _writer.GetSpan(10);
        var i = EncodeVarint(span, ProtoTag.Make(fieldNumber, ProtoWireType.LengthDelimited));
        i += EncodeVarint(span.Slice(i), (ulong)payloadLength);
        _writer.Advance(i);
    }
}
