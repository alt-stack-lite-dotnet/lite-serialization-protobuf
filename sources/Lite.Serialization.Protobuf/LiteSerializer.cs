using System;
using System.Buffers;
using Grpc.Core;

namespace Lite.Serialization.Protobuf;

/// <summary>
/// Span-first serializer facade. Every method takes a concrete <typeparamref name="T"/> at the call
/// site so the source generator can intercept it and emit a direct, allocation-free call. There is no
/// <c>byte[]</c>-returning API by design: to obtain owned bytes either write into a caller-provided
/// buffer (<see cref="SerializeTo{T}(in T, Span{byte})"/>) or rent one (<see cref="SerializeRented"/>).
/// </summary>
public static class LiteSerializer
{
    public static IProtoSerializer<T> For<T>() =>
        throw NotIntercepted(nameof(For), typeof(T));

    public static Marshaller<T> MarshallerFor<T>() =>
        throw NotIntercepted(nameof(MarshallerFor), typeof(T));

    /// <summary>
    /// Zero-allocation: writes the serialized bytes into the caller's <paramref name="destination"/> and
    /// returns the number of bytes written. Size it with <see cref="ComputeSize"/> — <c>stackalloc</c>
    /// for small payloads, <c>ArrayPool</c> for large ones. Intercepted into a direct call.
    /// </summary>
    public static int SerializeTo<T>(in T value, Span<byte> destination) =>
        throw NotIntercepted(nameof(SerializeTo), typeof(T));

    /// <summary>
    /// Serializes into an <see cref="IBufferWriter{T}"/> (pipelines, gRPC sinks). Intercepted into a direct call.
    /// </summary>
    public static void SerializeTo<T>(in T value, IBufferWriter<byte> writer) =>
        throw NotIntercepted(nameof(SerializeTo), typeof(T));

    /// <summary>
    /// Exact serialized size in bytes. Intercepted into a direct call.
    /// </summary>
    public static int ComputeSize<T>(in T value) =>
        throw NotIntercepted(nameof(ComputeSize), typeof(T));

    /// <summary>
    /// Serializes into a buffer rented from <see cref="MemoryPool{T}.Shared"/>. The returned
    /// <see cref="RentedBuffer"/> MUST be disposed to return the buffer to the pool.
    /// </summary>
    public static RentedBuffer SerializeRented<T>(in T value) =>
        throw NotIntercepted(nameof(SerializeRented), typeof(T));

    /// <summary>
    /// Deserializes from a contiguous span — zero-copy. Intercepted into a direct call.
    /// </summary>
    public static T DeserializeFrom<T>(ReadOnlySpan<byte> source) =>
        throw NotIntercepted(nameof(DeserializeFrom), typeof(T));

    /// <summary>
    /// Deserializes from a (possibly multi-segment) sequence — e.g. a gRPC payload. Intercepted into a direct call.
    /// </summary>
    public static T DeserializeFrom<T>(ReadOnlySequence<byte> source) =>
        throw NotIntercepted(nameof(DeserializeFrom), typeof(T));

    public static Marshaller<T> CreateMarshaller<T>(IProtoSerializer<T> serializer) =>
        new Marshaller<T>(
            serializer: (value, ctx) =>
            {
                serializer.WriteTo(value, ctx.GetBufferWriter());
                ctx.Complete(); // required: signals the gRPC SerializationContext the payload is done
            },
            deserializer: ctx => serializer.ReadFrom(ctx.PayloadAsReadOnlySequence())
        );

    private static InvalidOperationException NotIntercepted(string method, Type t) =>
        new($"LiteSerializer.{method}<{t.FullName}>() was not intercepted by the source generator. " +
            "Reference Lite.Serialization.Protobuf in the calling project so the analyzer runs and emits an interceptor. " +
            "Note: T must be a concrete type at the call site; generic type parameters are not supported.");
}
