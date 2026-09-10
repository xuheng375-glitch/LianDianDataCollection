using System;
using System.Globalization;
using System.Linq;
using System.Text;
using LianDian.Business.Services;
using LianDian.Business.Tests.Fakes;
using LianDian.Comm;
using LianDian.Core;
using LianDian.Core.Config;
using LianDian.Core.Models;
using Xunit;

namespace LianDian.Business.Tests
{
    public class ScientificNotationTests
    {
        [Theory]
        [InlineData("+5.00755E+03", "5007.55")]
        [InlineData("-1.23e-03", "-0.00123")]
        [InlineData("1", "1")]
        [InlineData("0E-50", "0")]
        [InlineData("-0.000E-100", "0")]
        [InlineData("1E-28", "0.0000000000000000000000000001")]
        [InlineData("  +5.00755E+03  ", "5007.55")]
        public void ParsesInvariantDecimal(string input, string expected)
        {
            Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), input.SafeToDecimal());
        }

        [Theory]
        [InlineData("5.00E+")]
        [InlineData("1E+100")]
        [InlineData("1E-100")]
        [InlineData("1E-29")]
        [InlineData("-1E-50")]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        [InlineData("5,007.55")]
        [InlineData("")]
        public void InvalidOrOverflowIsNotZero(string input) => Assert.Null(input.SafeToDecimal());

        [Fact]
        public void UnderflowDoesNotStoreOrAcknowledge()
        {
            var repo = new InMemoryBatchRepository();
            repo.Insert(new BatchRecord { BatchDate="26188", BatchNo=1, ProductName="P" });
            var plc = new FakePlcClient();
            using (var service = new WithstandService(plc, new FakeSnapshot(), repo, new BusinessConfig()))
                Assert.Throws<InvalidOperationException>(() => service.ProcessUploadForTest("26188", 1, "P", "1E-50", "1", "1", 1));
            Assert.Null(repo.Records.Single().Voltage);
            Assert.DoesNotContain(plc.Writes, w => w.Address == 4010);
        }

        [Fact]
        public void TwelveByteValueDecodesAndUploadsBeforeAck()
        {
            string input = "+5.00755E+03";
            var registers = ModbusCodec.EncodeString(input, 6, Encoding.UTF8, true);
            string decoded = ModbusCodec.DecodeString(registers, 0, 6, Encoding.UTF8, true);
            Assert.Equal(input, decoded);
            var repo = new InMemoryBatchRepository();
            repo.Insert(new BatchRecord { BatchDate="26188", BatchNo=1, ProductName="P" });
            var plc = new FakePlcClient();
            plc.BeforeWriteDInt = (address, value) =>
            {
                if (address == 4010 && value == 2)
                    Assert.Equal(5007.55m, repo.Records.Single().Voltage);
            };
            using (var service = new WithstandService(plc, new FakeSnapshot(), repo, new BusinessConfig()))
            {
                Assert.True(service.ProcessUploadForTest("26188", 1, "P", decoded, "+2.00000E+03", "+1.00000E-03", 1));
            }
            Assert.Equal(2000m, repo.Records.Single().Resistance);
            Assert.Equal(0.001m, repo.Records.Single().Current);
            Assert.Contains(plc.Writes, w => w.Address == 4010 && w.Value == 2);
        }
    }
}
