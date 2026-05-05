# Conventions (binder generation)

This page documents **what gets generated** and **how binders are named**.

## When a binder is generated

A binder is generated for a type if:

- the type has type-level `[FromBody]`, **or**
- at least one instance property has a Lite `From*` attribute (`FromRoute`, `FromQuery`, `FromBody`, etc.), **or**
- the type is targeted by a fluent `IRequestBindingConfiguration<TRequest>` (if present)

## Binder name

For a request model named:

```csharp
public sealed class CreateUserRequest { }
```

The generated binder is:

```csharp
public sealed class CreateUserRequestBinder { }
```

### Nested types

Nested types include all containing type names, joined with `_`:

```csharp
public sealed class Outer
{
    public sealed class InnerRequest { }
}
```

Binder name:

```csharp
public sealed class Outer_InnerRequestBinder { }
```

### Naming override

You can override the generated binder class name with:

```csharp
[Lite.Serialization.Protobuf.Attributes.BinderName("MyCustomBinder")]
public sealed class MyRequest { }
```

If you use a fluent binding configuration, you can also place the attribute on the configuration type:

```csharp
[Lite.Serialization.Protobuf.Attributes.BinderName("MyCustomBinder")]
public sealed class MyRequestBindingConfiguration
    : Lite.Serialization.Protobuf.Fluent.IRequestBindingConfiguration<MyRequest>
{
    public void Configure(Lite.Serialization.Protobuf.Fluent.IRequestBindingBuilder<MyRequest> builder)
    {
    }
}
```

## Binder namespace

Binders are generated into the **same namespace** as the request model.

If the request model is in the global namespace, the binder is also emitted into the global namespace.

## Generic request models

Open generic request models (e.g. `UpdateCommand<TPayload>`) are **skipped** by the generator.

Reason: generated binders are concrete types, and open generics make binder emission and registration ambiguous.

