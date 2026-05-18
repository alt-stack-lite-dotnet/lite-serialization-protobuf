using System;
using System.Buffers;
using Grpc.Core;
using Xunit;

namespace Lite.Serialization.Protobuf.Tests;

// Minimal gRPC contexts to drive a Marshaller<T> in tests.
file sealed class BufferSerializationContext : SerializationContext
{
    private readonly ArrayBufferWriter<byte> _w = new();
    public ReadOnlyMemory<byte> Written => _w.WrittenMemory;
    public override IBufferWriter<byte> GetBufferWriter() => _w;
    public override void Complete() { }
    public override void Complete(byte[] payload) { _w.Write(payload); }
    public override void SetPayloadLength(int payloadLength) { }
}

file sealed class SequenceDeserializationContext(ReadOnlyMemory<byte> payload) : DeserializationContext
{
    public override int PayloadLength => payload.Length;
    public override byte[] PayloadAsNewBuffer() => payload.ToArray();
    public override ReadOnlySequence<byte> PayloadAsReadOnlySequence() => new(payload);
}

public class GrpcInteropTests
{
    // Calls grpc::Marshallers.Create<T>(...) with DELIBERATELY BROKEN delegates.
    // If the SG interceptor swapped it for our marshaller, round-trip works.
    // If not intercepted, the broken delegates throw.
    [Fact]
    public void MarshallersCreate_IsInterceptedBy_OurSerializer()
    {
        var marshaller = Marshallers.Create<ExplicitTagMessage>(
            serializer: static (_, _) => throw new InvalidOperationException("Google serializer must not run"),
            deserializer: static _ => throw new InvalidOperationException("Google deserializer must not run"));

        var original = new ExplicitTagMessage { Id = 555, Label = "intercepted", Flag = true };

        var ser = new BufferSerializationContext();
        marshaller.ContextualSerializer(original, ser);

        var rt = marshaller.ContextualDeserializer(new SequenceDeserializationContext(ser.Written));

        Assert.Equal(555, rt.Id);
        Assert.Equal("intercepted", rt.Label);
        Assert.True(rt.Flag);
    }
}
