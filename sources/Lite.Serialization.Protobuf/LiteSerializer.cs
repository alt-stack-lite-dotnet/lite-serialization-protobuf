using System.Buffers;
using Grpc.Core;

namespace Lite.Serialization.Protobuf;

public static class LiteSerializer
{
    public static IProtoSerializer<T> For<T>() =>
        throw NotIntercepted(nameof(For), typeof(T));

    public static Marshaller<T> MarshallerFor<T>() =>
        throw NotIntercepted(nameof(MarshallerFor), typeof(T));

    /// <summary>
    /// Fast-path serialization. Intercepted by the source generator at compile time and rewritten
    /// to a direct static method call (no interface dispatch). For unknown T at call site, throws.
    /// </summary>
    public static void Serialize<T>(in T value, IBufferWriter<byte> writer) =>
        throw NotIntercepted(nameof(Serialize), typeof(T));

    /// <summary>
    /// Fast-path deserialization. Intercepted by the source generator at compile time and rewritten
    /// to a direct static method call (no interface dispatch). For unknown T at call site, throws.
    /// </summary>
    public static T Deserialize<T>(ReadOnlySequence<byte> source) =>
        throw NotIntercepted(nameof(Deserialize), typeof(T));

    public static T Deserialize<T>(ReadOnlySpan<byte> source) =>
        Deserialize<T>(new ReadOnlySequence<byte>(source.ToArray()));

    public static T Deserialize<T>(byte[] source) =>
        Deserialize<T>(new ReadOnlySequence<byte>(source));

    /// <summary>
    /// Allocates a single byte[] of exact size and serializes into it. One allocation per call.
    /// Intercepted by source generator into a direct two-pass call (size + write).
    /// </summary>
    public static byte[] Serialize<T>(in T value) =>
        throw NotIntercepted(nameof(Serialize), typeof(T));

    /// <summary>
    /// Zero-allocation: writes serialized bytes to caller's destination span and returns bytes written.
    /// Intercepted by source generator into a direct call.
    /// Caller is responsible for sizing destination — call ComputeSize first or use Serialize(in T) -> byte[].
    /// </summary>
    public static int SerializeTo<T>(in T value, Span<byte> destination) =>
        throw NotIntercepted(nameof(SerializeTo), typeof(T));

    /// <summary>
    /// Returns exact serialized size in bytes. Intercepted into direct call.
    /// </summary>
    public static int ComputeSize<T>(in T value) =>
        throw NotIntercepted(nameof(ComputeSize), typeof(T));

    /// <summary>
    /// Serializes into a buffer rented from <see cref="MemoryPool{T}.Shared"/>.
    /// Returns a <see cref="RentedBuffer"/> that the caller MUST dispose to return the buffer to the pool.
    /// Pure pool-backed: zero heap allocation per call (after warmup).
    /// </summary>
    public static RentedBuffer SerializeRented<T>(in T value) =>
        throw NotIntercepted(nameof(SerializeRented), typeof(T));

    public static Marshaller<T> CreateMarshaller<T>(IProtoSerializer<T> serializer) =>
        new Marshaller<T>(
            serializer: (value, ctx) => serializer.WriteTo(value, ctx.GetBufferWriter()),
            deserializer: ctx => serializer.ReadFrom(ctx.PayloadAsReadOnlySequence())
        );

    private static InvalidOperationException NotIntercepted(string method, Type t) =>
        new($"LiteSerializer.{method}<{t.FullName}>() was not intercepted by the source generator. " +
            "Reference Lite.Serialization.Protobuf in the calling project so the analyzer runs and emits an interceptor. " +
            "Note: T must be a concrete type at the call site; generic type parameters are not supported.");
}
