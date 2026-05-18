using Grpc.AspNetCore.Server.Model;
using Grpc.Core;
using Grpc.Net.Client;
using Lite.Serialization.Protobuf;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;

// A complete gRPC.NET service whose wire serialization is Lite.Serialization.Protobuf —
// no .proto, no Grpc.Tools, no Google.Protobuf. Server + client in one process.

const int port = 5199;
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

// ── Server ──
var builder = WebApplication.CreateBuilder();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.ConfigureKestrel(o =>
    o.ListenLocalhost(port, lo => lo.Protocols = HttpProtocols.Http2)); // h2c — plaintext HTTP/2

builder.Services.AddGrpc();
builder.Services.AddSingleton<GreeterService>();
// Bind the code-first service into gRPC.NET via its IServiceMethodProvider extension point:
builder.Services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IServiceMethodProvider<GreeterService>, GreeterMethodProvider>());

var app = builder.Build();
app.MapGrpcService<GreeterService>();
await app.StartAsync();
Console.WriteLine($"server: gRPC up on http://localhost:{port}  — serializer = Lite (not Google.Protobuf)");

// ── Client ──
using var channel = GrpcChannel.ForAddress($"http://localhost:{port}");
var invoker = channel.CreateCallInvoker();

foreach (var (name, loud) in new[] { ("Alice", false), ("Bob", true) })
{
    using var call = invoker.AsyncUnaryCall(
        GreeterContract.SayHello, host: null, new CallOptions(),
        new HelloRequest { Name = name, Loud = loud });
    var reply = await call.ResponseAsync;
    Console.WriteLine($"client: SayHello({name}, loud:{loud}) → \"{reply.Message}\"");
}

await app.StopAsync();
Console.WriteLine("done.");

// ── Shared contract: Method<,> built with Lite marshallers ──
public static class GreeterContract
{
    public static readonly Method<HelloRequest, HelloReply> SayHello = new(
        MethodType.Unary, "playground.Greeter", "SayHello",
        LiteSerializer.MarshallerFor<HelloRequest>(),    // ← Lite serializer plugs in here
        LiteSerializer.MarshallerFor<HelloReply>());     // ← and here
}

// ── Messages: plain classes (gRPC requires reference types for request/response) ──
public class HelloRequest
{
    public string Name { get; set; } = "";
    public bool Loud { get; set; }
}

public class HelloReply
{
    public string Message { get; set; } = "";
}

// ── Service implementation ──
public sealed class GreeterService
{
    public Task<HelloReply> SayHello(HelloRequest req, ServerCallContext ctx)
    {
        var msg = $"Hello, {req.Name}!";
        return Task.FromResult(new HelloReply { Message = req.Loud ? msg.ToUpperInvariant() : msg });
    }
}

// ── Binds the service into gRPC.NET routing using the Lite-backed Method<,> ──
public sealed class GreeterMethodProvider : IServiceMethodProvider<GreeterService>
{
    public void OnServiceMethodDiscovery(ServiceMethodProviderContext<GreeterService> ctx) =>
        ctx.AddUnaryMethod(
            GreeterContract.SayHello,
            metadata: Array.Empty<object>(),
            invoker: static (GreeterService svc, HelloRequest req, ServerCallContext c) => svc.SayHello(req, c));
}
