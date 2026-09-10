using LianDian.Business.Services;
using LianDian.Business.Tests.Fakes;
using LianDian.Core.Config;
using Xunit;

namespace LianDian.Business.Tests
{
    public class WithstandDiagnosticTests
    {
        [Fact]
        public void MissingDateReportsOnceWhileBusinessKeepsRetrying()
        {
            var snapshot = new FakeSnapshot();
            snapshot.SetDInt(4010, 1);
            var plc = new FakePlcClient();
            using (var service = new WithstandService(plc, snapshot, new InMemoryBatchRepository(), new BusinessConfig()))
            {
                int reports = 0;
                service.ErrorOccurred += (s, e) => { Assert.Contains("D5800", e.Message); reports++; };
                service.EnableForTest();
                service.TickOnce();
                service.TickOnce();
                Assert.Equal(1, reports);
                Assert.Empty(plc.Writes);
            }
        }
    }
}
