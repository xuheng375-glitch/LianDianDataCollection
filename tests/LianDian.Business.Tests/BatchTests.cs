using System;
using System.Linq;
using LianDian.Business.Services;
using LianDian.Business.Tests.Fakes;
using LianDian.Comm;
using LianDian.Core;
using LianDian.Core.Config;
using LianDian.Core.Models;
using Xunit;

namespace LianDian.Business.Tests
{
    public class BatchTests
    {
        private static BusinessConfig Config()
        {
            return new BusinessConfig
            {
                BatchResetTime = TimeSpan.Zero,
                HeartbeatIntervalMs = 100,
                PollIntervalMs = 100
            };
        }

        [Theory]
        [InlineData(2026, 7, 7, "26188")]   // 文档示例：年2位+天数3位
        [InlineData(2024, 1, 1, "24001")]
        [InlineData(2024, 12, 31, "24366")] // 闰年 366 天
        [InlineData(2023, 12, 31, "23365")]
        public void ToBatchDateString_EncodesYearAndDay(int year, int month, int day, string expected)
        {
            Assert.Equal(expected, new DateTime(year, month, day).ToBatchDateString());
        }

        [Fact]
        public void InitFromDatabase_ContinuesFromMaxBatchNo()
        {
            var repo = new InMemoryBatchRepository();
            var now = new DateTime(2026, 7, 7, 10, 0, 0);
            string date = now.ToBatchDateString();
            for (int i = 1; i <= 5; i++)
            {
                repo.Insert(new BatchRecord { BatchDate = date, BatchNo = i, ProductName = "P" + i });
                repo.ClearPendingAck(RegisterMap.D4002_BatchIssueFlag, date, i);
            }

            var service = new BatchService(new FakePlcClient(), new FakeSnapshot(), repo, Config(), () => now);
            service.InitFromDatabase();
            Assert.Equal(6, service.CurrentBatchNo);
        }

        [Fact]
        public void ProcessIssue_WritesRegisters_InsertsRecord_SetsFlag2_Increments()
        {
            var repo = new InMemoryBatchRepository();
            var plc = new FakePlcClient();
            var snapshot = new FakeSnapshot();
            snapshot.SetString(RegisterMap.D5000_ProductName, "P1");
            snapshot.SetWord(RegisterMap.D4030_EmployeeNo, 1234);
            snapshot.SetString(RegisterMap.D5000_ProductName, "HT11-ACBARBUS");
            var now = new DateTime(2026, 7, 7, 10, 0, 0);
            var service = new BatchService(plc, snapshot, repo, Config(), () => now);
            service.InitFromDatabase();

            bool ok = service.ProcessIssueForTest("26188", 3, "1234", "HT11-ACBARBUS");

            Assert.True(ok);
            Assert.Equal("26188", plc.ReadString(RegisterMap.D5700_BatchIssueDate, 3));
            Assert.Equal("00003", plc.ReadString(RegisterMap.D4100_BatchIssueNo, 3));
            Assert.Contains(plc.Writes, w => w.Address == RegisterMap.D4002_BatchIssueFlag && w.Value == (int)FlagState.Success);
            BatchRecord record = repo.Records.Single();
            Assert.Equal("26188", record.BatchDate);
            Assert.Equal(3, record.BatchNo);
            Assert.Equal("1234", record.EmployeeNo);
            Assert.Equal("HT11-ACBARBUS", record.ProductName);
            Assert.Null(record.Voltage);
            Assert.Equal(4, service.CurrentBatchNo);
        }

        [Fact]
        public void ProcessIssue_ConsecutiveIssues_Increment()
        {
            var repo = new InMemoryBatchRepository();
            var plc = new FakePlcClient();
            var snapshot = new FakeSnapshot();
            snapshot.SetString(RegisterMap.D5000_ProductName, "P1");
            var now = new DateTime(2026, 7, 7, 10, 0, 0);
            var service = new BatchService(plc, snapshot, repo, Config(), () => now);
            service.InitFromDatabase();

            service.ProcessIssueForTest("26188", 1, "1001", "P1");
            service.ProcessIssueForTest("26188", 2, "1002", "P2");

            Assert.Equal(2, repo.Records.Count);
            Assert.Equal(3, service.CurrentBatchNo);
            Assert.Equal(2, repo.Records.Count(r => r.BatchNo == 1 || r.BatchNo == 2));
        }

        [Fact]
        public void ProcessIssue_DuplicateKey_IsIdempotent()
        {
            var repo = new InMemoryBatchRepository();
            var plc = new FakePlcClient();
            var snapshot = new FakeSnapshot();
            snapshot.SetString(RegisterMap.D5000_ProductName, "P1");
            var now = new DateTime(2026, 7, 7, 10, 0, 0);
            var service = new BatchService(plc, snapshot, repo, Config(), () => now);
            service.InitFromDatabase();

            bool first = service.ProcessIssueForTest("26188", 1, "1001", "P1");
            bool second = service.ProcessIssueForTest("26188", 1, "1001", "P1");

            Assert.True(first);
            Assert.True(second);
            Assert.Single(repo.Records);
            Assert.Contains(plc.Writes, w => w.Address == RegisterMap.D4002_BatchIssueFlag && w.Value == (int)FlagState.Success);
        }

        [Fact]
        public void TickOnce_FlagNotWaiting_DoesNothing()
        {
            var repo = new InMemoryBatchRepository();
            var plc = new FakePlcClient();
            var snapshot = new FakeSnapshot();
            snapshot.SetString(RegisterMap.D5000_ProductName, "P1");
            snapshot.SetDInt(RegisterMap.D4002_BatchIssueFlag, 2);
            var now = new DateTime(2026, 7, 7, 10, 0, 0);
            var service = new BatchService(plc, snapshot, repo, Config(), () => now);
            service.EnableForTest();
            service.TickOnce();

            Assert.Empty(repo.Records);
            Assert.Empty(plc.StringWrites);
        }

        [Theory]
        [InlineData("A")]
        [InlineData("F")]
        public void TickOnce_IssuesWithoutBindingGrade(string grade)
        {
            var plc = new FakePlcClient();
            plc.BeforeReadString = address => { if (address == 6000) throw new InvalidOperationException("二维码不应在下发时读取"); };
            plc.SetString(RegisterMap.D6000_QrGrade, grade, 1);
            var snapshot = new FakeSnapshot();
            snapshot.SetString(RegisterMap.D5000_ProductName, "P1");
            snapshot.SetDInt(RegisterMap.D4002_BatchIssueFlag, 1);
            snapshot.SetDInt(RegisterMap.D4030_EmployeeNo, 1001);
            snapshot.SetDInt(RegisterMap.D4030_EmployeeNo, 600249);
            snapshot.SetString(RegisterMap.D6000_QrGrade, "B");
            var repo = new InMemoryBatchRepository();
            var service = new BatchService(plc, snapshot, repo, Config(), () => new DateTime(2026, 7, 7));
            service.InitFromDatabase();
            service.EnableForTest();
            service.TickOnce();
            Assert.Null(repo.Records.Single().QrGrade);
            Assert.Equal("600249", repo.Records.Single().EmployeeNo);
            Assert.Contains("D4100=00001", plc.StringWrites);
            Assert.Contains("D5700=26188", plc.StringWrites);
        }

        [Theory]
        [InlineData("")]
        [InlineData("G")]
        [InlineData("a")]
        public void TickOnce_InvalidGradeDoesNotBlockIssue(string grade)
        {
            var plc = new FakePlcClient();
            plc.SetString(RegisterMap.D6000_QrGrade, grade, 1);
            var snapshot = new FakeSnapshot();
            snapshot.SetString(RegisterMap.D5000_ProductName, "P1");
            snapshot.SetDInt(RegisterMap.D4002_BatchIssueFlag, 1);
            snapshot.SetDInt(RegisterMap.D4030_EmployeeNo, 1001);
            var repo = new InMemoryBatchRepository();
            var service = new BatchService(plc, snapshot, repo, Config(), () => new DateTime(2026, 7, 7));
            service.InitFromDatabase();
            service.EnableForTest();
            service.TickOnce();
            Assert.Null(Assert.Single(repo.Records).QrGrade);
            Assert.Equal(2, plc.StringWrites.Count);
            Assert.Contains(plc.Writes, w => w.Address == 4002 && w.Value == 2);
        }

        [Fact]
        public void TickOnce_FlagStaysOne_DoesNotDoubleIssue()
        {
            var repo = new InMemoryBatchRepository();
            var plc = new FakePlcClient();
            var snapshot = new FakeSnapshot();
            snapshot.SetString(RegisterMap.D5000_ProductName, "P1");
            snapshot.SetDInt(RegisterMap.D4002_BatchIssueFlag, 1);
            snapshot.SetDInt(RegisterMap.D4030_EmployeeNo, 1001);
            var now = new DateTime(2026, 7, 7, 10, 0, 0);
            var service = new BatchService(plc, snapshot, repo, Config(), () => now);
            service.InitFromDatabase();
            service.EnableForTest();

            service.TickOnce(); // 第一次下发
            Assert.Single(repo.Records);
            Assert.Equal(2, service.CurrentBatchNo);

            service.TickOnce(); // 快照仍为 1（陈旧）：不得重复下发
            Assert.Single(repo.Records);
            Assert.Equal(2, service.CurrentBatchNo);
        }

        [Fact]
        public void TickOnce_CrossDay_ResetsBatchNo()
        {
            var repo = new InMemoryBatchRepository();
            var plc = new FakePlcClient();
            var snapshot = new FakeSnapshot();
            snapshot.SetString(RegisterMap.D5000_ProductName, "P1");
            snapshot.SetDInt(RegisterMap.D4002_BatchIssueFlag, 1);
            snapshot.SetDInt(RegisterMap.D4030_EmployeeNo, 1001);
            // 前一天已下发 5 个批次，启动续号为 6
            for (int i = 1; i <= 5; i++)
            {
                repo.Insert(new BatchRecord { BatchDate = "26188", BatchNo = i, ProductName = "P" + i });
                repo.ClearPendingAck(RegisterMap.D4002_BatchIssueFlag, "26188", i);
            }
            var current = new DateTime(2026, 7, 7, 23, 59, 59);
            var service = new BatchService(plc, snapshot, repo, Config(), () => current);
            service.InitFromDatabase();
            Assert.Equal(6, service.CurrentBatchNo);

            current = new DateTime(2026, 7, 8, 0, 0, 1);
            service.EnableForTest();
            service.TickOnce();

            // 跨天重置为 1 后立即下发当日批次 1
            Assert.Equal(2, service.CurrentBatchNo);
            BatchRecord issued = repo.Records.Single(r => r.BatchDate == "26189");
            Assert.Equal(1, issued.BatchNo);
        }
    }
}
