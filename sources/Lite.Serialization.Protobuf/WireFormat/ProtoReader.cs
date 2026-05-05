using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace Lite.Serialization.Protobuf.WireFormat;

public ref struct ProtoReader
{
    private SequenceReader<byte> _reader;

    public ProtoReader(ReadOnlySequence<byte> source)
    {
        _reader = new SequenceReader<byte>(source);
    }

    public bool End => _reader.End;

    public long BytesConsumed => _reader.Consumed;

    public bool TryReadTag(out int fieldNumber, out ProtoWireType wireType)
    {
        if (_reader.End)
        {
            fieldNumber = 0;
            wireType = default;
            return false;
        }
        var tag = (uint)ReadRawVarint();
        var split = ProtoTag.Split(tag);
        fieldNumber = split.FieldNumber;
        wireType = split.WireType;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong ReadRawVarint()
    {
        ulong value = 0;
        var shift = 0;
        while (shift < 64)
        {
            if (!_reader.TryRead(out var b))
                ThrowEof();
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return value;
            shift += 7;
        }
        throw new InvalidDataException("Varint too long.");
    }

    public int ReadInt32() => (int)(long)ReadRawVarint();
    public long ReadInt64() => (long)ReadRawVarint();
    public uint ReadUInt32() => (uint)ReadRawVarint();
    public ulong ReadUInt64() => ReadRawVarint();

    public bool ReadBool()
    {
        if (!_reader.TryRead(out var b))
            ThrowEof();
        return b != 0;
    }

    public double ReadDouble()
    {
        Span<byte> tmp = stackalloc byte[8];
        if (!_reader.TryCopyTo(tmp))
            ThrowEof();
        _reader.Advance(8);
        return BinaryPrimitives.ReadDoubleLittleEndian(tmp);
    }

    public float ReadFloat()
    {
        Span<byte> tmp = stackalloc byte[4];
        if (!_reader.TryCopyTo(tmp))
            ThrowEof();
        _reader.Advance(4);
        return BinaryPrimitives.ReadSingleLittleEndian(tmp);
    }

    public uint ReadFixed32() => (uint)(_reader.TryReadLittleEndian(out int v) ? v : ThrowEofInt32());
    public ulong ReadFixed64() => (ulong)(_reader.TryReadLittleEndian(out long v) ? v : ThrowEofInt64());

    public string ReadString()
    {
        var length = (int)ReadRawVarint();
        if (length == 0) return string.Empty;
        var unread = _reader.UnreadSequence.Slice(0, length);
        string result;
        if (unread.IsSingleSegment)
        {
            result = Encoding.UTF8.GetString(unread.FirstSpan);
        }
        else
        {
            var rented = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                unread.CopyTo(rented);
                result = Encoding.UTF8.GetString(rented, 0, length);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
        _reader.Advance(length);
        return result;
    }

    public byte[] ReadBytes()
    {
        var length = (int)ReadRawVarint();
        if (length == 0) return Array.Empty<byte>();
        var buf = new byte[length];
        if (!_reader.TryCopyTo(buf))
            ThrowEof();
        _reader.Advance(length);
        return buf;
    }

    public void ReadFixedLengthBytes(scoped Span<byte> destination)
    {
        var length = (int)ReadRawVarint();
        if (length != destination.Length)
            throw new InvalidDataException($"Expected fixed-length payload of {destination.Length} bytes, got {length}.");
        if (!_reader.TryCopyTo(destination))
            ThrowEof();
        _reader.Advance(length);
    }

    public ReadOnlySequence<byte> ReadLengthDelimitedSlice()
    {
        var length = (int)ReadRawVarint();
        var slice = _reader.UnreadSequence.Slice(0, length);
        _reader.Advance(length);
        return slice;
    }

    public T ReadMessage<T>(IProtoSerializer<T> serializer) =>
        serializer.ReadFrom(ReadLengthDelimitedSlice());

    public void SkipField(ProtoWireType wireType)
    {
        switch (wireType)
        {
            case ProtoWireType.Varint:
                ReadRawVarint();
                return;
            case ProtoWireType.Fixed32:
                _reader.Advance(4);
                return;
            case ProtoWireType.Fixed64:
                _reader.Advance(8);
                return;
            case ProtoWireType.LengthDelimited:
                var length = (long)ReadRawVarint();
                _reader.Advance(length);
                return;
            default:
                throw new InvalidDataException($"Unknown wire type: {wireType}");
        }
    }

    private static int ThrowEofInt32() { ThrowEof(); return 0; }
    private static long ThrowEofInt64() { ThrowEof(); return 0L; }
    private static void ThrowEof() =>
        throw new InvalidDataException("Unexpected end of protobuf payload.");
}
