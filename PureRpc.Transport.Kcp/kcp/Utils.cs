using System.Runtime.CompilerServices;

namespace PureRpc.Transport.Kcp
{
    public static partial class Utils
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Encode8u(byte[] p, int offset, byte value)
        {
            p[offset] = value;
            return 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Decode8u(byte[] p, int offset, out byte value)
        {
            value = p[offset];
            return 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Encode16U(byte[] p, int offset, ushort value)
        {
            p[offset] = (byte)(value >> 0);
            p[offset + 1] = (byte)(value >> 8);
            return 2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Decode16U(byte[] p, int offset, out ushort value)
        {
            value = (ushort)(p[offset] | (p[offset + 1] << 8));
            return 2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Encode32U(byte[] p, int offset, uint value)
        {
            p[offset] = (byte)(value >> 0);
            p[offset + 1] = (byte)(value >> 8);
            p[offset + 2] = (byte)(value >> 16);
            p[offset + 3] = (byte)(value >> 24);
            return 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Decode32U(byte[] p, int offset, out uint value)
        {
            value = p[offset] | (uint)(p[offset + 1] << 8) | (uint)(p[offset + 2] << 16) | (uint)(p[offset + 3] << 24);
            return 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int TimeDiff(uint later, uint earlier)
        {
            return (int)(later - earlier);
        }
    }
}
