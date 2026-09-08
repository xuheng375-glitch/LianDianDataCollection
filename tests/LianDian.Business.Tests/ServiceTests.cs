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
    public class ServiceTests
    {
        private static BusinessConfig Config()
        {
            return new BusinessConfig { HeartbeatIntervalMs = 100, PollIntervalMs = 100 };
        }

        private static InMemoryBatchRepository SeedBatch(string date, int no, string product)
        {
            var repo = new InMemoryBatchRepository();
            repo.Insert(new BatchRecord { BatchDate = date, BatchNo = no, ProductName = product, IssueTime = DateTime.Now });
            return repo;
        }

        // ---------- 心跳 ----------

        [Fact]
        public void Heartbeat_PlcDisconnected_StopsAndDoesNotWrite()
        {
            var plc = new FakePlcClient(); // 未连接
            var service = new HeartbeatService(plc, () => true, Config());
            service.EnableForTest();
            service.TickOnce();

            Assert.Equal(HeartbeatState.Stopped, service.State);
            Assert.Empty(plc.Writes);
        }

        [Fact]
        public void Heartbeat_DbUnhealthy_StopsSending()
        {
            var plc = new FakePlcClient();
            plc.Connect();
            var service = new HeartbeatService(plc, () => false, Config());
            service.EnableForTest();
            service.TickOnce();

            Assert.Equal(HeartbeatState.Stopped, service.State);
            Assert.Empty(plc.Writes);
        }

        [Fact]
        public void Heartbeat_Healthy_AlternatesZeroOne()
        {
            var plc = new FakePlcClient();
            plc.Connect();
            var service = new HeartbeatService(plc, () => true, Config());
            service.EnableForTest();

            service.TickOnce();
            service.TickOnce();
            service.TickOnce();

            Assert.Equal(HeartbeatState.Running, service.State);
            var writes = plc.Writes.Where(w => w.Address == RegisterMap.D4000_Heartbeat).Select(w => w.Value).ToList();
            Assert.Equal(new[] { 1, 0, 1 }, writes); // 初始 0 → 首写 1，再取反
        }

        [Fact]
        public void Heartbeat_Recovery_ResumesSending()
        {
            var plc = new FakePlcClient();
            plc.Connect();
            bool healthy = false;
            var service = new HeartbeatService(plc, () => healthy, Config());
            service.EnableForTest();

            service.TickOnce();
            Assert.Empty(plc.Writes);
            Assert.Equal(HeartbeatState.Stopped, service.State);

            healthy = true;
            service.TickOnce();
            Assert.Single(plc.Writes);
            Assert.Equal(HeartbeatState.Running, service.State);
        }

        // ---------- 耐压 ----------

        [Fact]
        public void Withstand_Match_UpdatesSameBatch_Flag2()
        {
            var repo = SeedBatch("26188", 3, "HT11-ACBARBUS");
            var plc = new FakePlcClient();
            var snapshot = new FakeSnapshot();
            snapshot.SetDInt(RegisterMap.D4010_WithstandFlag, (int)FlagState.Waiting);
            snapshot.SetString(RegisterMap.D5800_WithstandDate, "26188");
            snapshot.SetString(RegisterMap.D4200_WithstandBatchNo, "00003");
            snapshot.SetDInt(RegisterMap.D4014_WithstandResult, (int)TestResult.Ok);
            snapshot.SetString(RegisterMap.D5100_WithstandProductName, "HT11-ACBARBUS");
            snapshot.SetString(RegisterMap.D5300_Voltage, "220.5");
            snapshot.SetString(RegisterMap.D5400_Resistance, "10.2");
            snapshot.SetString(RegisterMap.D5500_Current, "0.8");

            var service = new WithstandService(plc, snapshot, repo, Config());
            service.EnableForTest();
            service.TickOnce();

            BatchRecord r = repo.Records.Single();
            Assert.Equal(220.5m, r.Voltage);
            Assert.Equal(10.2m, r.Resistance);
            Assert.Equal(0.8m, r.Current);
            Assert.Equal((short)TestResult.Ok, r.WithstandResult);
            Assert.NotNull(r.WithstandTime);
            Assert.Contains(plc.Writes, w => w.Address == RegisterMap.D4010_WithstandFlag && w.Value == (int)FlagState.Success);
        }

        [Fact]
        public void Withstand_ExistingData_SetsFlag3()
        {
            var repo = SeedBatch("26188", 3, "HT11-ACBARBUS");
            repo.Records.Single().WithstandResult = (short)TestResult.Ok; // 历史数据
            var plc = new FakePlcClient();
            var snapshot = new FakeSnapshot();
            snapshot.SetDInt(RegisterMap.D4010_WithstandFlag, (int)FlagState.Waiting);
            snapshot.SetString(RegisterMap.D5800_WithstandDate, "26188");
            snapshot.SetString(RegisterMap.D4200_WithstandBatchNo, "00003");
            snapshot.SetDInt(RegisterMap.D4014_WithstandResult, (int)TestResult.Ok);
            snapshot.SetString(RegisterMap.D5100_WithstandProductName, "HT11-ACBARBUS");

            var service = new WithstandService(plc, snapshot, repo, Config());
            service.EnableForTest();
            service.TickOnce();

            Assert.Contains(plc.Writes, w => w.Address == RegisterMap.D4010_WithstandFlag && w.Value == (int)FlagState.Error);
        }

        [Fact]
        public void Withstand_Mismatch_KeepsFlag1()
        {
            var repo = SeedBatch("26188", 3, "HT11-ACBARBUS");
            var plc = new FakePlcClient();
            var service = new WithstandService(plc, new FakeSnapshot(), repo, Config());
            service.EnableForTest();

            bool ok = service.ProcessUploadForTest("26188", 3, "OTHER-PRODUCT", "220.5", "10.2", "0.8", 1);

            Assert.False(ok);
            Assert.DoesNotContain(plc.Writes, w => w.Address == RegisterMap.D4010_WithstandFlag);
        }

        [Fact]
        public void Withstand_FlagStaysOne_DoesNotDoubleProcess()
        {
            var repo = SeedBatch("26188", 3, "HT11-ACBARBUS");
            var plc = new FakePlcClient();
            var snapshot = new FakeSnapshot();
            snapshot.SetDInt(RegisterMap.D4010_WithstandFlag, (int)FlagState.Waiting);
            snapshot.SetString(RegisterMap.D5800_WithstandDate, "26188");
            snapshot.SetString(RegisterMap.D4200_WithstandBatchNo, "00003");
            snapshot.SetDInt(RegisterMap.D4014_WithstandResult, (int)TestResult.Ok);
            snapshot.SetString(RegisterMap.D5100_WithstandProductName, "HT11-ACBARBUS");
            snapshot.SetString(RegisterMap.D5300_Voltage, "220.5");
            snapshot.SetString(RegisterMap.D5400_Resistance, "10.2");
            snapshot.SetString(RegisterMap.D5500_Current, "0.8");

            var service = new WithstandService(plc, snapshot, repo, Config());
            service.EnableForTest();
            service.TickOnce(); // 第一次处理
            Assert.Equal((short)TestResult.Ok, repo.Records.Single().WithstandResult);
            int writesAfterFirst = plc.Writes.Count(w => w.Address == RegisterMap.D4010_WithstandFlag);
            Assert.Equal(1, writesAfterFirst);

            service.TickOnce(); // 快照仍为 1（陈旧）：不得重复处理
            Assert.Equal(writesAfterFirst, plc.Writes.Count(w => w.Address == RegisterMap.D4010_WithstandFlag));

            // PLC 清零后重新置 1（上升沿）→ 再次处理，此时判定历史数据 → 标志位=3
            snapshot.SetDInt(RegisterMap.D4010_WithstandFlag, 0);
            service.TickOnce();
            snapshot.SetDInt(RegisterMap.D4010_WithstandFlag, (int)FlagState.Waiting);
            service.TickOnce();
            Assert.Contains(plc.Writes, w => w.Address == RegisterMap.D4010_WithstandFlag && w.Value == (int)FlagState.Error);
        }

        // ---------- 气压 ----------

        [Fact]
        public void Pressure_Match_UpdatesSameBatch_Flag2()
        {
            var repo = SeedBatch("26188", 4, "HT11-ACBARBUS");
            var plc = new FakePlcClient();
            var snapshot = new FakeSnapshot();
            snapshot.SetDInt(RegisterMap.D4020_PressureFlag, (int)FlagState.Waiting);
            snapshot.SetString(RegisterMap.D5900_PressureDate, "26188");
            snapshot.SetString(RegisterMap.D4300_PressureBatchNo, "00004");
            snapshot.SetDInt(RegisterMap.D4024_PressureResult, (int)TestResult.Ng);
            snapshot.SetString(RegisterMap.D5200_PressureProductName, "HT11-ACBARBUS");
            snapshot.SetString(RegisterMap.D5600_PressureValue, "0.5");

            var service = new PressureService(plc, snapshot, repo, Config());
            service.EnableForTest();
            service.TickOnce();

            BatchRecord r = repo.Records.Single();
            Assert.Equal(0.5m, r.Pressure);
            Assert.Equal((short)TestResult.Ng, r.PressureResult);
            Assert.NotNull(r.PressureTime);
            Assert.Contains(plc.Writes, w => w.Address == RegisterMap.D4020_PressureFlag && w.Value == (int)FlagState.Success);
        }

        [Fact]
        public void Pressure_ExistingData_SetsFlag3()
        {
            var repo = SeedBatch("26188", 4, "HT11-ACBARBUS");
            repo.Records.Single().PressureResult = (short)TestResult.Ok;
            var plc = new FakePlcClient();
            var snapshot = new FakeSnapshot();
            snapshot.SetDInt(RegisterMap.D4020_PressureFlag, (int)FlagState.Waiting);
            snapshot.SetString(RegisterMap.D5900_PressureDate, "26188");
            snapshot.SetString(RegisterMap.D4300_PressureBatchNo, "00004");
            snapshot.SetDInt(RegisterMap.D4024_PressureResult, (int)TestResult.Ok);
            snapshot.SetString(RegisterMap.D5200_PressureProductName, "HT11-ACBARBUS");

            var service = new PressureService(plc, snapshot, repo, Config());
            service.EnableForTest();
            service.TickOnce();

            Assert.Contains(plc.Writes, w => w.Address == RegisterMap.D4020_PressureFlag && w.Value == (int)FlagState.Error);
        }

        [Fact]
        public void Pressure_Mismatch_KeepsFlag1()
        {
            var repo = SeedBatch("26188", 4, "HT11-ACBARBUS");
            var plc = new FakePlcClient();
            var service = new PressureService(plc, new FakeSnapshot(), repo, Config());
            service.EnableForTest();

            bool ok = service.ProcessUploadForTest("26188", 4, "WRONG", "0.5", 1);

            Assert.False(ok);
            Assert.DoesNotContain(plc.Writes, w => w.Address == RegisterMap.D4020_PressureFlag);
        }
    }
}
