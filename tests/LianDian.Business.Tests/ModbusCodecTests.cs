using System.Text;
using LianDian.Business.Tests.Fakes;
using LianDian.Comm;
using Xunit;

namespace LianDian.Business.Tests
{
    public class ModbusCodecTests
    {
        [Fact]
        public void DInt_LowWordFirst_RoundTrip()
        {
            int value = 0x01020304;
            ushort[] regs = ModbusCodec.FromDInt(value, lowWordFirst: true);
            Assert.Equal(new ushort[] { 0x0304, 0x0102 }, regs);
            Assert.Equal(value, ModbusCodec.ToDInt(regs, 0, lowWordFirst: true));
        }

        [Fact]
        public void DInt_HighWordFirst_RoundTrip()
        {
            int value = 0x01020304;
            ushort[] regs = ModbusCodec.FromDInt(value, lowWordFirst: false);
            Assert.Equal(new ushort[] { 0x0102, 0x0304 }, regs);
            Assert.Equal(value, ModbusCodec.ToDInt(regs, 0, lowWordFirst: false));
        }

        [Fact]
        public void String_LowByteFirst_DecodesProductName()
        {
            const string product = "HT11-ACBARBUS";
            ushort[] regs = FakePlcClient.EncodeString(product, 10, lowByteFirst: true);
            string decoded = ModbusCodec.DecodeString(regs, 0, regs.Length, Encoding.UTF8, lowByteFirst: true);
            Assert.Equal(product, decoded);
        }

        [Fact]
        public void String_WrongByteOrder_ProducesDifferentText()
        {
            const string product = "HT11-ACBARBUS";
            ushort[] regs = FakePlcClient.EncodeString(product, 10, lowByteFirst: true);
            string wrong = ModbusCodec.DecodeString(regs, 0, regs.Length, Encoding.UTF8, lowByteFirst: false);
            Assert.NotEqual(product, wrong);
            Assert.Contains("TH", wrong); // 经典乱序：HT11 → TH11
        }

        [Fact]
        public void TrimEndNull_StopsAtFirstNull()
        {
            Assert.Equal("ABC", ModbusCodec.TrimEndNull("ABC\0DEF\0"));
            Assert.Equal("ABC", ModbusCodec.TrimEndNull("ABC"));
            Assert.Equal(string.Empty, ModbusCodec.TrimEndNull("\0"));
            Assert.Equal(string.Empty, ModbusCodec.TrimEndNull(null));
        }
    }
}
