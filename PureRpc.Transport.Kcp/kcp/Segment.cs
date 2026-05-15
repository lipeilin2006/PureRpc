using System.Buffers.Binary;

namespace PureRpc.Transport.Kcp
{
    // KCP Segment Definition
    internal class Segment
    {
        internal uint conv;
        internal uint cmd;
        internal uint frg;
        internal uint wnd;
        internal uint ts;
        internal uint sn;
        internal uint una;
        internal uint resendts;
        internal int  rto;
        internal uint fastack;
        internal uint xmit;

        internal byte[] data = new byte[Kcp.MTU_DEF];
        internal int length;

        // ikcp_encode_seg: encode segment fields into buffer.
        // returns bytes written (always 24).
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
