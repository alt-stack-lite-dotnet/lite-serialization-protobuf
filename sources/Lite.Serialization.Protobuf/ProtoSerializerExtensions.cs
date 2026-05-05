using System.Buffers;

namespace Lite.Serialization.Protobuf;

public static class ProtoSerializerExtensions
{
    public static byte[] Serialize<T>(this IProtoSerializer<T> serializer, T value)
    {
        var buf = new ArrayBufferWriter<byte>();
        serializer.WriteTo(value, buf);
        return buf.WrittenSpan.ToArray();
    }

    public static void SerializeTo<T>(this IProtoSerializer<T> serializer, T value, IBufferWriter<byte> writer) =>
        serializer.WriteTo(value, writer);

    public static T Deserialize<T>(this IProtoSerializer<T> serializer, ReadOnlySpan<byte> data)
    {
        var arr = data.ToArray();
        return serializer.ReadFrom(new ReadOnlySequence<byte>(arr));
    }

    public static T Deserialize<T>(this IProtoSerializer<T> serializer, byte[] data) =>
        serializer.ReadFrom(new ReadOnlySequence<byte>(data));

    public static T Deserialize<T>(this IProtoSerializer<T> serializer, ReadOnlyMemory<byte> data) =>
        serializer.ReadFrom(new ReadOnlySequence<byte>(data));

    public static T Deserialize<T>(this IProtoSerializer<T> serializer, ReadOnlySequence<byte> data) =>
        serializer.ReadFrom(data);
}
