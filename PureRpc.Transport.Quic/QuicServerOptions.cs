using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace PureRpc.Transport.Quic;

public class QuicServerOptions
{
    public EndPoint ListenEndPoint { get; set; } = new IPEndPoint(IPAddress.Any, 5035);
    public X509Certificate2? ServerCertificate { get; set; }
    public string? ServerCertificatePath { get; set; }
    public string? ServerCertificatePassword { get; set; }
    public int MaxInboundBidirectionalStreams { get; set; } = 10000;
    public int Backlog { get; set; } = 512;
}
