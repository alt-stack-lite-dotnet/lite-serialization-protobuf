# Fluent Configuration

By default Lite needs no configuration. When you need to control how a type maps to the wire —
override a field's tag or proto name, or drop a field — implement
`IProtoSerializerConfiguration<T>`. The source generator discovers it automatically (no registration).

```csharp
using Lite.Serialization.Protobuf.Fluent;

public sealed class UserConfig : IProtoSerializerConfiguration<User>
{
    public void Configure(IProtoSerializerBuilder<User> b)
    {
        b.Field(x => x.Id).Tag(1);                 // pin the field number
        b.Field(x => x.DisplayName).Name("name");  // override the proto field name
        b.Field(x => x.InternalNote).Ignore();     // exclude from serialization entirely
    }
}
```

| Method | Effect |
|---|---|
| `.Tag(int)` | set the proto field number (overrides const/name-hash) |
| `.Name(string)` | set the proto field name in the generated schema |
| `.Ignore()` | exclude the member from the wire and the schema |

The configuration is applied at compile time — there is no runtime cost and no DI wiring. A type's
config is found purely by the interface implementation existing in a referenced assembly.

## Custom value conversion

`IProtoSerializerBuilder<T>.Convert<TClr, TWire>` registers a conversion between a CLR type and its
wire representation (e.g. store a domain type as a primitive):

```csharp
public void Configure(IProtoSerializerBuilder<Order> b) =>
    b.Convert<Money, long>(m => m.Cents, cents => Money.FromCents(cents));
```

## Precedence

When deciding a field's tag, Lite uses, in order:

1. Fluent `.Tag(n)` (this file)
2. `const int XxxFieldNumber` on the type (the Grpc.Tools convention)
3. FNV-1a hash of the field's proto name (deterministic default)

For anything that crosses a wire or evolves over time, set explicit numbers (1 or 2) — see
[Wire Compatibility](Wire-Compatibility.md) and [Practices](Practices.md).
