using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace PureRpc.Transport.Quic;

public class QuicClientOptions
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 5035;
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(15);
    public RemoteCertificateValidationCallback? CertificateValidationCallback { get; set; }

    public EndPoint RemoteEndPoint =>
        IPAddress.TryParse(Host, out var ip)
            ? new IPEndPoint(ip, Port)
            : new DnsEndPoint(Host, Port);
}
