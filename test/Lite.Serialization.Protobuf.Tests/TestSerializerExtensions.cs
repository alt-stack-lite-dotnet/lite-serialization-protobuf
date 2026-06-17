using System;
using System.Buffers;

namespace Lite.Serialization.Protobuf.Tests;

// Test-only convenience over IProtoSerializer<T>. The shipped API is span-first (no byte[]); these
// byte[] helpers keep round-trip assertions terse and live ONLY in the test assembly.
internal static class TestSerializerExtensions
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
