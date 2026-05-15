namespace PureRpc.Transport.Http2;

public class Http2ServerOptions
{
    public int Port { get; set; } = 5001;
    /// <summary>使用 TLS 时指定证书路径。</summary>
    public string? TlsCertPath { get; set; }
    public string? TlsKeyPath { get; set; }
}
