using System;
using System.Linq.Expressions;

namespace Lite.Serialization.Protobuf.Fluent;

public interface IProtoSerializerBuilder<T>
{
    IFieldBuilder<T, TField> Field<TField>(Expression<Func<T, TField>> selector);

    IProtoSerializerBuilder<T> Convert<TClr, TWire>(
        Func<TClr, TWire> toWire,
        Func<TWire, TClr> fromWire);
}
