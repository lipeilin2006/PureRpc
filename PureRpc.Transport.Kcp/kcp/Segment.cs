using System.Buffers.Binary;

namespace PureRpc.Transport.Kcp
{
    /// <summary>
    /// KCP 段定义，用于内部消息分片和重传管理 / 
    /// KCP Segment Definition, used for internal message fragmentation and retransmission management.
    /// </summary>
    internal class Segment
    {
        /// <summary>
        /// 会话标识符 / Conversation identifier.
        /// </summary>
        internal uint conv;

        /// <summary>
        /// 命令类型 / Command type.
        /// </summary>
        internal uint cmd;

        /// <summary>
        /// 分片编号（逆序） / Fragment number (in reverse order).
        /// </summary>
        internal uint frg;

        /// <summary>
        /// 窗口大小 / Window size.
        /// </summary>
        internal uint wnd;

        /// <summary>
        /// 时间戳 / Timestamp.
        /// </summary>
        internal uint ts;

        /// <summary>
        /// 序列号 / Sequence number.
        /// </summary>
        internal uint sn;

        /// <summary>
        /// 未确认序列号 / Unacknowledged sequence number.
        /// </summary>
        internal uint una;

        /// <summary>
        /// 重发时间戳 / Resend timestamp.
        /// </summary>
        internal uint resendts;

        /// <summary>
        /// 重传超时时间 / Retransmission timeout.
        /// </summary>
        internal int  rto;

        /// <summary>
        /// 快速重传确认计数 / Fast retransmission acknowledgment count.
        /// </summary>
        internal uint fastack;

        /// <summary>
        /// 发送次数 / Transmission count.
        /// </summary>
        internal uint xmit;

        /// <summary>
        /// 段数据缓冲区 / Segment data buffer.
        /// </summary>
        internal byte[] data = new byte[Kcp.MTU_DEF];

        /// <summary>
        /// 段数据长度 / Segment data length.
        /// </summary>
        internal int length;

        /// <summary>
        /// 将段头部字段编码到缓冲区 / Encodes segment header fields into the buffer.
        /// </summary>
        /// <param name="ptr">目标缓冲区 / Target buffer.</param>
        /// <param name="offset">起始偏移量 / Starting offset.</param>
        /// <returns>写入的字节数（始终为 24）/ Bytes written (always 24).</returns>
        internal int Encode(byte[] ptr, int offset)
        {
            var span = ptr.AsSpan(offset);
            BinaryPrimitives.WriteUInt32LittleEndian(span, conv);
            span[4] = (byte)cmd;
            span[5] = (byte)frg;
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(6, 2), (ushort)wnd);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8, 4), ts);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(12, 4), sn);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(16, 4), una);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(20, 4), (uint)length);
            return 24;
        }

        /// <summary>
        /// 重置段状态以便对象池复用 / Resets segment state for object pool reuse.
        /// </summary>
        internal void Reset()
        {
            conv = 0;
            cmd = 0;
            frg = 0;
            wnd = 0;
            ts  = 0;
            sn  = 0;
            una = 0;
            rto = 0;
            xmit = 0;
            resendts = 0;
            fastack  = 0;
            length = 0;
        }
    }
}
