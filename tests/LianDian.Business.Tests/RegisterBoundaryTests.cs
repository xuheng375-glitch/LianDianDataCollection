using System;
using LianDian.Comm;
using LianDian.Core.Config;
using Xunit;

namespace LianDian.Business.Tests
{
    public class RegisterBoundaryTests
    {
        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, -1)]
        [InlineData(int.MaxValue, int.MaxValue)]
        [InlineData(65537, 0)]
        public void RejectsInvalidMapping(int address, int offset) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => RegisterMap.ToModbus(address, offset));

        [Fact]
        public void ValidEndpointsAndExistingMappingUnchanged()
        {
            Assert.Equal((ushort)0, RegisterMap.ToModbus(1, 0));
            Assert.Equal(ushort.MaxValue, RegisterMap.ToModbus(65536, 0));
            Assert.Equal((ushort)4030, RegisterMap.ToModbus(4030, 1));
        }

        [Theory]
        [InlineData(4002, 0)]
        [InlineData(4002, -1)]
        [InlineData(4002, 126)]
        [InlineData(65535, 2)]
        public void InvalidReadFailsBeforeAnyConnection(int address, int count)
        {
            using (var plc = new ModbusTcpPlcClient(new PlcConfig { ModbusOffset = 1 }))
                Assert.Throws<ArgumentOutOfRangeException>(() => plc.ReadRegisters(address, count));
        }

        [Fact]
        public void InvalidDIntWriteDoesNotAttemptConnection()
        {
            using (var plc = new ModbusTcpPlcClient(new PlcConfig { ModbusOffset = 1 }))
                Assert.Throws<ArgumentOutOfRangeException>(() => plc.WriteDInt(65535, 1));
        }
    }
}
