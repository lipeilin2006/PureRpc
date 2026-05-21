using System.Runtime.CompilerServices;

namespace PureRpc.Transport.Kcp
{
    /// <summary>
    /// KCP 工具类，提供编码、解码和时间差计算功能 / 
    /// KCP utility class providing encoding, decoding, and time difference calculation.
    /// </summary>
    public static partial class Utils
    {
        /// <summary>
        /// 将值限制在指定范围内 / Clamps a value within the specified range.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>
        /// 将单个字节编码到缓冲区 / Encodes a single byte to the buffer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Encode8u(byte[] p, int offset, byte value)
        {
            p[offset] = value;
            return 1;
        }

        /// <summary>
        /// 从缓冲区解码单个字节 / Decodes a single byte from the buffer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Decode8u(byte[] p, int offset, out byte value)
        {
            value = p[offset];
            return 1;
        }

        /// <summary>
        /// 将 16 位无符号整数编码到缓冲区（小端序）/ Encodes a 16-bit unsigned integer to the buffer (little-endian).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Encode16U(byte[] p, int offset, ushort value)
        {
            p[offset] = (byte)(value >> 0);
            p[offset + 1] = (byte)(value >> 8);
            return 2;
        }

        /// <summary>
        /// 从缓冲区解码 16 位无符号整数（小端序）/ Decodes a 16-bit unsigned integer from the buffer (little-endian).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Decode16U(byte[] p, int offset, out ushort value)
        {
            value = (ushort)(p[offset] | (p[offset + 1] << 8));
            return 2;
        }

        /// <summary>
        /// 将 32 位无符号整数编码到缓冲区（小端序）/ Encodes a 32-bit unsigned integer to the buffer (little-endian).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Encode32U(byte[] p, int offset, uint value)
        {
            p[offset] = (byte)(value >> 0);
            p[offset + 1] = (byte)(value >> 8);
            p[offset + 2] = (byte)(value >> 16);
            p[offset + 3] = (byte)(value >> 24);
            return 4;
        }

        /// <summary>
        /// 从缓冲区解码 32 位无符号整数（小端序）/ Decodes a 32-bit unsigned integer from the buffer (little-endian).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Decode32U(byte[] p, int offset, out uint value)
        {
            value = p[offset] | (uint)(p[offset + 1] << 8) | (uint)(p[offset + 2] << 16) | (uint)(p[offset + 3] << 24);
            return 4;
        }

        /// <summary>
        /// 计算两个时间戳之间的差值（考虑无符号整数环绕）/ 
        /// Computes the difference between two timestamps (accounting for unsigned integer wrapping).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int TimeDiff(uint later, uint earlier)
        {
            return (int)(later - earlier);
        }
    }
}
