using System;
using System.Text;

namespace LianDian.Comm
{
    /// <summary>Modbus 数据编解码：DINT 字序、字符串字节序、空字符截断。</summary>
    public static class ModbusCodec
    {
        /// <summary>两个 16 位寄存器 → 32 位有符号整数。</summary>
        public static int ToDInt(ushort[] regs, int offset, bool lowWordFirst)
        {
            if (regs == null) throw new ArgumentNullException(nameof(regs));
            if (offset < 0 || offset + 1 >= regs.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            uint value = lowWordFirst
                ? (uint)(regs[offset] | (regs[offset + 1] << 16))
                : (uint)((regs[offset] << 16) | regs[offset + 1]);
            return unchecked((int)value);
        }

        /// <summary>32 位有符号整数 → 两个 16 位寄存器。</summary>
        public static ushort[] FromDInt(int value, bool lowWordFirst)
        {
            uint v = unchecked((uint)value);
            ushort low = (ushort)(v & 0xFFFF);
            ushort high = (ushort)((v >> 16) & 0xFFFF);
            return lowWordFirst ? new[] { low, high } : new[] { high, low };
        }

        /// <summary>寄存器区 → 字节流（每寄存器 2 字节，字内字节序可配）。</summary>
        public static byte[] ToBytes(ushort[] regs, int offset, int count, bool lowByteFirst)
        {
            if (regs == null) throw new ArgumentNullException(nameof(regs));
            if (offset < 0 || count < 0 || offset + count > regs.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            var bytes = new byte[count * 2];
            for (int i = 0; i < count; i++)
            {
                ushort r = regs[offset + i];
                byte lo = (byte)(r & 0xFF);
                byte hi = (byte)((r >> 8) & 0xFF);
                if (lowByteFirst)
                {
                    bytes[i * 2] = lo;
                    bytes[i * 2 + 1] = hi;
                }
                else
                {
                    bytes[i * 2] = hi;
                    bytes[i * 2 + 1] = lo;
                }
            }
            return bytes;
        }

        /// <summary>寄存器区 → 字符串（解码 + 首个 \0 截断）。</summary>
        public static string DecodeString(ushort[] regs, int offset, int count, Encoding encoding, bool lowByteFirst)
        {
            if (encoding == null) throw new ArgumentNullException(nameof(encoding));
            byte[] bytes = ToBytes(regs, offset, count, lowByteFirst);
            string decoded = encoding.GetString(bytes);
            return TrimEndNull(decoded);
        }

        /// <summary>字符串 → 寄存器区，用于实际 PLC 字符串写入，与 DecodeString 互逆。</summary>
        public static ushort[] EncodeString(string value, int registerCount, Encoding encoding, bool lowByteFirst)
        {
            if (registerCount < 0) throw new ArgumentOutOfRangeException(nameof(registerCount));
            if (encoding == null) throw new ArgumentNullException(nameof(encoding));
            var regs = new ushort[registerCount];
            if (string.IsNullOrEmpty(value)) return regs;
            byte[] bytes = encoding.GetBytes(value);
            for (int i = 0; i < regs.Length; i++)
            {
                byte b0 = i * 2 < bytes.Length ? bytes[i * 2] : (byte)0;
                byte b1 = i * 2 + 1 < bytes.Length ? bytes[i * 2 + 1] : (byte)0;
                regs[i] = lowByteFirst ? (ushort)(b0 | (b1 << 8)) : (ushort)((b0 << 8) | b1);
            }
            return regs;
        }

        /// <summary>兼容默认 UTF-8 调用；生产通信应显式传入配置编码。</summary>
        public static ushort[] EncodeString(string value, int registerCount, bool lowByteFirst)
            => EncodeString(value, registerCount, Encoding.UTF8, lowByteFirst);

        /// <summary>在首个 '\0' 处截断，避免尾部乱码。</summary>
        public static string TrimEndNull(string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
            int idx = value.IndexOf('\0');
            return idx >= 0 ? value.Substring(0, idx) : value;
        }
    }
}
