using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace PureRpc.Transport.Http3;

public class Http3ClientOptions
{
    public string Url { get; set; } = "https://127.0.0.1:5002/rpc";
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool>? ServerCertificateValidation { get; set; }
}
