# Dependency Injection

## Lite needs no DI

`LiteSerializer` is static and the per-type serializers are generated at compile time as singletons.
There is nothing to register, no container, no runtime resolution on the serialization path:

```csharp
byte[] bytes = LiteSerializer.Serialize<User>(in user);
User back = LiteSerializer.Deserialize<User>(bytes);
IProtoSerializer<User> s = LiteSerializer.For<User>();   // cached singleton instance
Marshaller<User> m = LiteSerializer.MarshallerFor<User>(); // static marshaller
```

`For<T>()` and `MarshallerFor<T>()` return the same generated instances every call — safe to capture in
a `static readonly` field.

## Where DI does appear: gRPC.NET

The serializer is DI-free, but binding a code-first service into gRPC.NET uses the standard
gRPC.NET DI extension points — that's the host's container, not Lite's:

```csharp
builder.Services.AddGrpc();
builder.Services.AddSingleton<GreeterService>();
builder.Services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IServiceMethodProvider<GreeterService>, GreeterMethodProvider>());
```

The `Method<,>` your provider binds carries Lite marshallers (`LiteSerializer.MarshallerFor<T>()`).
See [gRPC](gRPC.md) for the full pattern.

## Consumer project setup

The NuGet package's `buildTransitive` props/targets wire the source-generator interceptors
automatically — no manual `<InterceptorsNamespaces>` or `<CompilerVisibleProperty>` needed. The only
opt-in property is `LiteSerializerInterceptGrpc` (see [gRPC](gRPC.md)).
