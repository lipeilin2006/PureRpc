using System;
using System.Net;
using PureRpc.Abstractions;

namespace PureRpc.Transport.Tcp
{
    public class TcpClientOptions
    {
        public string Host { get; set; } = "127.0.0.1";

        public int Port { get; set; } = 5000;

        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(15);

        public bool NoDelay { get; set; } = true;

        public int SendBufferSize { get; set; } = 64 * 1024;

        public int ReceiveBufferSize { get; set; } = 64 * 1024;

        public EndPoint RemoteEndPoint => EndPointHelper.ResolveEndPoint(Host, Port);

        public string? TargetHost { get; set; }
    }
}