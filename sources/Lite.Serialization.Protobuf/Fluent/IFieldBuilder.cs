namespace Lite.Serialization.Protobuf.Fluent;

public interface IFieldBuilder<T, TField>
{
    IFieldBuilder<T, TField> Tag(int tag);

    IFieldBuilder<T, TField> Name(string protoName);

    IFieldBuilder<T, TField> Ignore();
}
