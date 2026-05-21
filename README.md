# PureRpc

<div id="badges">
	<a href="https://www.nuget.org/packages/PureRpc"><img src="https://img.shields.io/nuget/vpre/PureRpc?style=for-the-badge" alt="NuGet Pre"/></a>
	<a href="https://www.nuget.org/packages/PureRpc"><img src="https://img.shields.io/nuget/dt/PureRpc?style=for-the-badge" alt="NuGet Downloads"/></a>
	<a href="https://github.com/lipeilin2006/PureRpc/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/lipeilin2006/PureRpc/ci.yml?style=for-the-badge" alt="CI"/></a>
	<a href="https://github.com/lipeilin2006/PureRpc/blob/main/LICENSE"><img src="https://img.shields.io/github/license/lipeilin2006/PureRpc?style=for-the-badge" alt="License"/></a>
</div>

A high-performance, modular RPC framework for .NET with pluggable transports, serializers, interceptors, a C# source generator, and built-in authorization/metrics.

## Features

- **Pluggable architecture** — swap transports and serializers independently
- **C# source generator** — `[RpcService]` interface → client proxy + server dispatcher, zero runtime reflection
- **Pipeline interceptors** — client-side and server-side middleware chain
- **Authorization** — `[Authorize]` / `[AllowAnonymous]`, role/policy-based, `IAuthorizationHandler`, JWT Bearer middleware
- **Metrics** — `System.Diagnostics.Metrics` (`rpc.server.*`, `rpc.client.*`) for call count, latency, errors, in-flight requests
- **Cancellation** — per-request `CancellationToken` propagation, cancel frame support
- **DI integration** — `IHostedService` auto-start, `IServiceCollection` builder pattern

## Quick Start

```csharp
// 1. Define a service interface
[RpcService("calc")]
public interface ICalcService
{
    [RpcMethod]
    ValueTask<int> Add(int x, int y, CancellationToken ct = default);
}

// 2. Implement
public class CalcService : ServiceBase, ICalcService
{
    public ValueTask<int> Add(int x, int y, CancellationToken ct) =>
        new(x + y);
}

// 3. Server
var host = Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddPureRpcServer()
            .WithTcpTransport(5000)
            .WithJsonSerializer()
            .WithCalcService<CalcService>();
    })
    .Build();
host.Run();

// 4. Client
using var host = Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddPureRpcClient()
            .WithTcpTransport("127.0.0.1:5000")
            .WithJsonSerializer()
            .WithCalcServiceProxy();
    })
    .Build();
await host.StartAsync();

var client = host.Services.GetRequiredService<ICalcService>();
var result = await client.Add(1, 2);
Console.WriteLine(result); // 3
```

## Transports

| Package | Description | TLS | Cancel |
|---------|-------------|-----|--------|
| `PureRpc.Transport.Tcp` | TCP, `PipeReader`/`PipeWriter`, optional `SslStream` | ✔ | ✔ |
| `PureRpc.Transport.Websocket` | WebSocket via `HttpListener` | ✗ | ✔ |
| `PureRpc.Transport.Kcp` | Reliable UDP (custom KCP) | ✗ | ✔ |
| `PureRpc.Transport.Http2` | HTTP/2 via ASP.NET Core Kestrel | ✔ | ✔ |
| `PureRpc.Transport.Quic` | QUIC (`System.Net.Quic`, TLS 1.3 required) | ✔ | ✔ |

## Serializers

| Package | Format |
|---------|--------|
| `PureRpc.Serialization.Json` | JSON (`System.Text.Json`) |
| `PureRpc.Serialization.MessagePack` | MessagePack |
| `PureRpc.Serialization.MemoryPack` | MemoryPack |
| `PureRpc.Serialization.Protobuf` | Protocol Buffers (`protobuf-net`) |

## Authentication

| Package | Description |
|---------|-------------|
| `PureRpc.Authentication.JwtBearer` | JWT Bearer token validation via `TokenValidationParameters` or OIDC Authority |

All auth packages are in `PureRpc.Abstractions`:
- `IAuthorizationHandler` / `AuthorizationHandlerBase` — custom handler or inline delegate
- `[Authorize(Policy, Roles)]` — service-level and method-level, multiple attributes (AND), `AllowMultiple = true`
- `[AllowAnonymous]` — method-level opt-out
- `ClientAuthorizationInterceptor` / `ServerAuthorizationInterceptor` — token injection and validation
- `AuthorizationPolicy` — fluent API (`RequireRole`, `RequireClaim`, `RequireAssertion`)
- `AuthorizationOptions` — named policy registration

## Project Structure

```
PureRpc.Abstractions/          # Core interfaces: ISerializer, IRpcClient, IRpcServer,
                               # IAuthorizationHandler, RpcContext, ServiceBase
PureRpc/                       # Core engine: RpcServer, RpcClient, interceptors,
                               # DI extensions, auth attributes, metrics
PureRpc.Generator/             # C# source generator (incremental)
PureRpc.Serialization.*/       # Serializer implementations
PureRpc.Transport.*/           # Transport implementations
PureRpc.Authentication.JwtBearer/ # JWT Bearer middleware
```

## License

MIT
