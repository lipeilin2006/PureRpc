namespace PureRpc.Transport.Http3;

public class Http3ServerOptions
{
    public int Port { get; set; } = 5002;
    public string TlsCertPath { get; set; } = null!;
    public string TlsKeyPath { get; set; } = null!;
}
