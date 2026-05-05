namespace Lite.Serialization.Protobuf.Fluent;

public interface IProtoSerializerConfiguration<T>
{
    void Configure(IProtoSerializerBuilder<T> builder);
}
