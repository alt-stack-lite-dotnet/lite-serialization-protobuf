using System;
using System.Buffers;
using Lite.Serialization.Protobuf;

namespace Lite.Serialization.Protobuf.Benchmarks;

// Bench-only convenience over IProtoSerializer<T>. The shipped API is span-first (no byte[]); these
// helpers exist only to set up fixtures and the "produce a byte[]" comparison rows.
internal static class BenchSerializerExtensions
{
    public static byte[] Serialize<T>(this IProtoSerializer<T> s, T value)
    {
        var w = new ArrayBufferWriter<byte>();
        s.WriteTo(value, w);
        return w.WrittenSpan.ToArray();
    }

    public static T Deserialize<T>(this IProtoSerializer<T> s, byte[] data) =>
        s.ReadFrom(new ReadOnlySequence<byte>(data));

    public static T Deserialize<T>(this IProtoSerializer<T> s, ReadOnlySpan<byte> data) =>
        s.ReadFrom(new ReadOnlySequence<byte>(data.ToArray()));
}
