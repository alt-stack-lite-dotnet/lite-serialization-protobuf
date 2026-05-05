using System.Buffers;

namespace Lite.Serialization.Protobuf;

public interface IProtoSerializer<T>
{
    void WriteTo(in T value, IBufferWriter<byte> writer);

    T ReadFrom(ReadOnlySequence<byte> source);
}
