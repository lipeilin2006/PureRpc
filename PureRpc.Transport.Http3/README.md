# PureRpc.Transport.Http3

HTTP/3 (QUIC) transport for PureRpc — RPC over HTTP/3 using ASP.NET Core Minimal API and Kestrel.

## Requirements

- TLS certificate (QUIC requires TLS 1.3)
- .NET 10+

## Usage

```
builder.AddPureRpcServer()
    .WithHttp3Transport(5002, o =>
    {
        o.TlsCertPath = "cert.pfx";
        o.TlsKeyPath = "key.pem";
    });

builder.AddPureRpcClient()
    .WithHttp3Transport("https://127.0.0.1:5002/rpc");
```
