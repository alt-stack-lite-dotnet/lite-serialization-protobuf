using System;
using System.Buffers;

namespace Lite.Serialization.Protobuf;

// Convenience over the IProtoSerializer<T> interface (the path used by gRPC marshallers and by callers
// that hold a serializer instance). Span-first, no byte[]: for a contiguous span use the intercepted
// LiteSerializer.SerializeTo / DeserializeFrom statics (zero-copy); these wrap the interface's
// IBufferWriter / ReadOnlySequence shapes.
public static class ProtoSerializerExtensions
{
    public static void SerializeTo<T>(this IProtoSerializer<T> serializer, in T value, IBufferWriter<byte> writer) =>
        serializer.WriteTo(value, writer);

    public static T DeserializeFrom<T>(this IProtoSerializer<T> serializer, ReadOnlySequence<byte> data) =>
        serializer.ReadFrom(data);

    public static T DeserializeFrom<T>(this IProtoSerializer<T> serializer, ReadOnlyMemory<byte> data) =>
        serializer.ReadFrom(new ReadOnlySequence<byte>(data));
}
