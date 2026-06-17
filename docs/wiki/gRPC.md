# gRPC

Lite gives gRPC.NET a `Grpc.Core.Marshaller<T>` for your POCO messages. **The service layer is
yours** — Lite serializes messages, you bind methods/streaming/clients.

## Marshaller for a message

```csharp
Marshaller<HelloRequest> m = LiteSerializer.MarshallerFor<HelloRequest>();
```

Use it to build a `Method<TRequest, TResponse>`:

```csharp
public static class GreeterContract
{
    public static readonly Method<HelloRequest, HelloReply> SayHello = new(
        MethodType.Unary, "playground.Greeter", "SayHello",
        LiteSerializer.MarshallerFor<HelloRequest>(),   // ← Lite serializes the request
        LiteSerializer.MarshallerFor<HelloReply>());     // ← and the reply
}
```

Messages are plain reference types (gRPC.NET requires `class` request/response):

```csharp
public class HelloRequest { public string Name { get; set; } = ""; public bool Loud { get; set; } }
public class HelloReply   { public string Message { get; set; } = ""; }
```

## Code-first server binding

Bind a code-first service into gRPC.NET via its `IServiceMethodProvider<TService>` extension point:

```csharp
public sealed class GreeterMethodProvider : IServiceMethodProvider<GreeterService>
{
    public void OnServiceMethodDiscovery(ServiceMethodProviderContext<GreeterService> ctx) =>
        ctx.AddUnaryMethod(
            GreeterContract.SayHello,
            metadata: Array.Empty<object>(),
            invoker: static (GreeterService svc, HelloRequest req, ServerCallContext c) => svc.SayHello(req, c));
}

// Registration
builder.Services.AddGrpc();
builder.Services.AddSingleton<GreeterService>();
builder.Services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IServiceMethodProvider<GreeterService>, GreeterMethodProvider>());
```

No `.proto`, no `Grpc.Tools`, no `Google.Protobuf` — just `Grpc.AspNetCore.Server` + Lite. A complete
runnable example is in [`playground/Grpc`](../../playground/Grpc).

> Streaming, client proxies and the contract shape are transport concerns the consumer owns. Each
> streamed message is still a normal proto message marshalled by Lite.

## Interop with Grpc.Tools-generated code

If you already have `Grpc.Tools`-generated messages that call `grpc::Marshallers.Create<T>()`, Lite can
intercept those call sites and substitute its serializer (wire format stays compatible). Opt in:

```xml
<PropertyGroup>
  <LiteSerializerInterceptGrpc>true</LiteSerializerInterceptGrpc>
</PropertyGroup>
```

Messages whose shape Lite can't serialize yet are left to Google.Protobuf (reported as info
`LITEGRPC100`), so the switch is safe to flip on a mixed codebase.
