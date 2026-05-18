using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace Lite.Serialization.Protobuf.WireFormat;

/// <summary>
/// Span-backed protobuf reader. Used internally by source-generated ReadFromSpan.
/// All hot methods are AggressiveInlining; no virtual calls in the hot path.
/// </summary>
public ref struct SpanProtoReader
{
    private ReadOnlySpan<byte> _span;
    private int _pos;

    public SpanProtoReader(ReadOnlySpan<byte> source)
    {
        _span = source;
        _pos = 0;
    }

    public bool End
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _pos >= _span.Length;
    }

    public int BytesConsumed
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _pos;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadTag(out int fieldNumber, out ProtoWireType wireType)
    {
        if (_pos >= _span.Length)
        {
            fieldNumber = 0;
            wireType = default;
            return false;
        }
        var tag = (uint)ReadRawVarint();
        fieldNumber = (int)(tag >> 3);
        wireType = (ProtoWireType)(tag & 7);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong ReadRawVarint()
    {
        ulong value = 0;
        var shift = 0;
        while (shift < 64)
        {
            if (_pos >= _span.Length) ThrowEof();
            var b = _span[_pos++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
            shift += 7;
        }
        throw new InvalidDataException("Varint too long.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadInt32() => (int)(long)ReadRawVarint();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ReadInt64() => (long)ReadRawVarint();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadUInt32() => (uint)ReadRawVarint();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong ReadUInt64() => ReadRawVarint();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ReadBool()
    {
        if (_pos >= _span.Length) ThrowEof();
        return _span[_pos++] != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ReadFloat()
    {
        var v = BinaryPrimitives.ReadSingleLittleEndian(_span.Slice(_pos));
        _pos += 4;
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ReadDouble()
    {
        var v = BinaryPrimitives.ReadDoubleLittleEndian(_span.Slice(_pos));
        _pos += 8;
        return v;
    }

    public string ReadString()
    {
        var length = (int)ReadRawVarint();
        if (length == 0) return string.Empty;
        var slice = _span.Slice(_pos, length);
        _pos += length;
        return Encoding.UTF8.GetString(slice);
    }

    public byte[] ReadBytes()
    {
        var length = (int)ReadRawVarint();
        if (length == 0) return Array.Empty<byte>();
        var slice = _span.Slice(_pos, length);
        _pos += length;
        return slice.ToArray();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadFixedLengthBytes(scoped Span<byte> destination)
    {
        var length = (int)ReadRawVarint();
        if (length != destination.Length)
            throw new InvalidDataException($"Expected fixed-length payload of {destination.Length} bytes, got {length}.");
        _span.Slice(_pos, length).CopyTo(destination);
        _pos += length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> ReadLengthDelimitedSpan()
    {
        var length = (int)ReadRawVarint();
        var slice = _span.Slice(_pos, length);
        _pos += length;
        return slice;
    }

    public void SkipField(ProtoWireType wireType)
    {
        switch (wireType)
        {
            case ProtoWireType.Varint:
                ReadRawVarint();
                return;
            case ProtoWireType.Fixed32:
                _pos += 4;
                return;
            case ProtoWireType.Fixed64:
                _pos += 8;
                return;
            case ProtoWireType.LengthDelimited:
                var length = (int)ReadRawVarint();
                _pos += length;
                return;
            default:
                throw new InvalidDataException($"Unknown wire type: {wireType}");
        }
    }

    private static void ThrowEof() =>
        throw new InvalidDataException("Unexpected end of protobuf payload.");
}
