using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace PureRpc.Transport.Kcp
{
    /// <summary>
    /// KCP 通用辅助工具 / KCP common helper utilities.
    /// 提供主机名解析、Socket 缓冲区配置和安全 Cookie 生成等功能 / 
    /// Provides hostname resolution, socket buffer configuration, and secure cookie generation.
    /// </summary>
    public static class Common
    {
        /// <summary>
        /// 解析主机名为 IP 地址数组（阻塞式） / Resolves a hostname to IP address arrays (blocking).
        /// </summary>
        /// <param name="hostname">主机名或 IP 地址字符串 / Hostname or IP address string.</param>
        /// <param name="addresses">解析结果 IP 地址数组 / The resolved IP address array.</param>
        /// <returns>解析成功返回 true；失败返回 false / True if resolution succeeded; false otherwise.</returns>
        public static bool ResolveHostname(string hostname, out IPAddress[] addresses)
        {
            try
            {
                // NOTE: dns lookup is blocking. this can take a second.
                addresses = Dns.GetHostAddresses(hostname);
                return addresses.Length >= 1;
            }
            catch (SocketException exception)
            {
                Log.Info($"[KCP] Failed to resolve host: {hostname} reason: {exception}");
                addresses = null;
                return false;
            }
        }

        /// <summary>
        /// 异步解析主机名为 IP 地址数组 / Asynchronously resolves a hostname to IP address arrays.
        /// </summary>
        /// <param name="hostname">主机名或 IP 地址字符串 / Hostname or IP address string.</param>
        /// <returns>解析结果 IP 地址数组 / The resolved IP address array.</returns>
        public static async Task<IPAddress[]> ResolveHostnameAsync(string hostname)
        {
            try
            {
                return await Dns.GetHostAddressesAsync(hostname).ConfigureAwait(false);
            }
            catch (SocketException exception)
            {
                Log.Info($"[KCP] Failed to resolve host: {hostname} reason: {exception}");
                return [];
            }
        }

        /// <summary>
        /// 配置 Socket 的收发缓冲区大小 / Configures socket receive and send buffer sizes.
        /// 如果连接在高负载下断开，请增加到操作系统限制 / 
        /// If connections drop under heavy load, increase to OS limit.
        /// </summary>
        /// <param name="socket">要配置的 Socket / The socket to configure.</param>
        /// <param name="recvBufferSize">接收缓冲区大小 / Receive buffer size.</param>
        /// <param name="sendBufferSize">发送缓冲区大小 / Send buffer size.</param>
        public static void ConfigureSocketBuffers(Socket socket, int recvBufferSize, int sendBufferSize)
        {
            // log initial size for comparison.
            // remember initial size for log comparison
            int initialReceive = socket.ReceiveBufferSize;
            int initialSend    = socket.SendBufferSize;

            // set to configured size
            try
            {
                socket.ReceiveBufferSize = recvBufferSize;
                socket.SendBufferSize    = sendBufferSize;
            }
            catch (SocketException)
            {
                Log.Warning($"[KCP] failed to set Socket RecvBufSize = {recvBufferSize} SendBufSize = {sendBufferSize}");
            }


            Log.Info($"[KCP] RecvBuf = {initialReceive}=>{socket.ReceiveBufferSize} ({socket.ReceiveBufferSize/initialReceive}x) SendBuf = {initialSend}=>{socket.SendBufferSize} ({socket.SendBufferSize/initialSend}x)");
        }

        /// <summary>
        /// 从 IP+端口生成连接哈希 / Generates a connection hash from IP + port.
        /// </summary>
        /// <param name="endPoint">远程端点 / The remote endpoint.</param>
        /// <returns>端点的哈希码 / The endpoint's hash code.</returns>
        public static int ConnectionHash(EndPoint endPoint) =>
            endPoint.GetHashCode();

        /// <summary>
        /// 使用安全随机数生成器生成连接 Cookie / Generates a connection cookie using a secure random number generator.
        /// Cookie 用于防止 UDP 伪装攻击 / Cookies are used to prevent UDP spoofing attacks.
        /// </summary>
        /// <returns>随机生成的 32 位无符号整数 / A randomly generated 32-bit unsigned integer.</returns>
        public static uint GenerateCookie()
        {
            Span<byte> buf = stackalloc byte[4];
            RandomNumberGenerator.Fill(buf);
            return BitConverter.ToUInt32(buf);
        }
    }
}
