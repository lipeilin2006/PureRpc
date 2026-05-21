using System;

namespace PureRpc.Transport.Kcp
{
    /// <summary>
    /// KCP 可靠消息头部枚举 / KCP reliable message header enum.
    /// 定义 KCP 可靠通道上的消息类型 / 
    /// Defines message types on the KCP reliable channel.
    /// </summary>
    public enum KcpHeaderReliable : byte
    {
        /// <summary>
        /// 握手消息 / Handshake message.
        /// </summary>
        Hello      = 1,

        /// <summary>
        /// Ping 消息（保活和 RTT 计算）/ Ping message (keep-alive and RTT calculation).
        /// </summary>
        Ping       = 2,

        /// <summary>
        /// Pong 响应消息 / Pong response message.
        /// </summary>
        Pong       = 4,

        /// <summary>
        /// 数据消息 / Data message.
        /// </summary>
        Data       = 3,
    }

    /// <summary>
    /// KCP 不可靠消息头部枚举 / KCP unreliable message header enum.
    /// 定义 KCP 不可靠通道上的消息类型 / 
    /// Defines message types on the KCP unreliable channel.
    /// </summary>
    public enum KcpHeaderUnreliable : byte
    {
        /// <summary>
        /// 不可靠数据消息 / Unreliable data message.
        /// </summary>
        Data = 4,

        /// <summary>
        /// 断开连接消息（快速发送多次以确保送达）/ Disconnect message (sent multiple times rapidly to ensure delivery).
        /// </summary>
        Disconnect = 5,
    }

    /// <summary>
    /// KCP 头部解析工具类 / KCP header parsing utility class.
    /// 提供头部字节值与枚举类型之间的安全转换 / 
    /// Provides safe conversion between header byte values and enum types.
    /// </summary>
    public static class KcpHeader
    {
        /// <summary>
        /// 安全地将字节值解析为可靠头部枚举 / Safely parses a byte value into a reliable header enum.
        /// </summary>
        /// <param name="value">头部字节值 / The header byte value.</param>
        /// <param name="header">解析结果 / The parsed result.</param>
        /// <returns>解析成功返回 true；无效值返回 false / True if parsing succeeded; false for invalid values.</returns>
        public static bool ParseReliable(byte value, out KcpHeaderReliable header)
        {
            switch (value)
            {
                case 1: header = KcpHeaderReliable.Hello;  return true;
                case 2: header = KcpHeaderReliable.Ping;   return true;
                case 3: header = KcpHeaderReliable.Data;   return true;
                case 4: header = KcpHeaderReliable.Pong;   return true;
                default:
                    header = KcpHeaderReliable.Ping;
                    return false;
            }
        }

        /// <summary>
        /// 安全地将字节值解析为不可靠头部枚举 / Safely parses a byte value into an unreliable header enum.
        /// </summary>
        /// <param name="value">头部字节值 / The header byte value.</param>
        /// <param name="header">解析结果 / The parsed result.</param>
        /// <returns>解析成功返回 true；无效值返回 false / True if parsing succeeded; false for invalid values.</returns>
        public static bool ParseUnreliable(byte value, out KcpHeaderUnreliable header)
        {
            switch (value)
            {
                case 4: header = KcpHeaderUnreliable.Data;       return true;
                case 5: header = KcpHeaderUnreliable.Disconnect; return true;
                default:
                    header = KcpHeaderUnreliable.Disconnect;
                    return false;
            }
        }
    }
}
