using LianDian.Business.Services;
using LianDian.Business.Tests.Fakes;
using LianDian.Core.Config;
using LianDian.Core.Models;
using Xunit;

namespace LianDian.Business.Tests
{
    public class PressureLogTests
    {
        [Theory]
        [InlineData(1, "产品名称不匹配")]
        [InlineData(2, "未找到对应日期和批次号")]
        public void MismatchReportsDetailsWithoutAcknowledging(int batch, string reason)
        {
            var repo = new InMemoryBatchRepository();
            repo.Insert(new BatchRecord { BatchDate="26188", BatchNo=1, ProductName="P1" });
            var plc = new FakePlcClient();
            using (var service = new PressureService(plc, new FakeSnapshot(), repo, new BusinessConfig()))
            {
                string message = null;
                service.DataMismatch += (s, e) => message = e.Message;
                Assert.False(service.ProcessUploadForTest("26188", batch, "P2", "1", 1));
                Assert.Contains(reason, message);
                Assert.Contains("D5200=P2", message);
                Assert.Contains("D4020=1", message);
                Assert.Empty(plc.Writes);
            }
        }

        [Fact]
        public void MissingDateReportsErrorInsteadOfSilentlyReturning()
        {
            var snapshot = new FakeSnapshot(); snapshot.SetDInt(4020, 1);
            var plc = new FakePlcClient();
            using (var service = new PressureService(plc, snapshot, new InMemoryBatchRepository(), new BusinessConfig()))
            {
                string message = null;
                service.ErrorOccurred += (s, e) => message = e.Message;
                service.EnableForTest(); service.TickOnce();
                Assert.Contains("D5900", message);
                Assert.Empty(plc.Writes);
            }
        }
    }
}
