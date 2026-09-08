using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LianDian.Business.Services;
using LianDian.Business.Tests.Fakes;
using LianDian.Comm;
using LianDian.Core;
using LianDian.Core.Config;
using LianDian.Core.Models;
using LianDian.Data;
using Xunit;

namespace LianDian.Business.Tests
{
    public sealed class StabilityTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "LianDianStabilityTests", Guid.NewGuid().ToString("N"));
        private DbConfig Config() => new DbConfig { FilePath = Path.Combine(_dir,"main.db"), BackupDirectory = Path.Combine(_dir,"backups"), MinimumFreeSpaceMb = 1 };
        private BatchRepository Repository()
        {
            var cfg = Config(); new DatabaseInitializer(cfg).EnsureCreated();
            return new BatchRepository(new DataContext(cfg));
        }

        [Fact]
        public void SnapshotIsImmutableAndExpires()
        {
            var raw = new Dictionary<int, ushort[]> { [4030] = ModbusCodec.FromDInt(600249,true) };
            var snapshot = new PlcSnapshot(raw, new PlcConfig { SnapshotMaxAgeMs=10 });
            raw[4030][0]=0;
            Assert.True(snapshot.TryGetDInt(4030,out int employee));
            Assert.Equal(600249,employee);
            Thread.Sleep(30);
            Assert.False(snapshot.TryGetDInt(4030,out _));
        }

        [Fact]
        public async Task SerialTimerDoesNotOverlapAndDisposeDrains()
        {
            int active=0, overlap=0, calls=0;
            using (var started=new ManualResetEventSlim())
            using (var release=new ManualResetEventSlim())
            {
                var timer=new SerialTimer(_ =>
                {
                    if(Interlocked.Increment(ref active)>1) Interlocked.Increment(ref overlap);
                    Interlocked.Increment(ref calls); started.Set(); release.Wait(3000); Interlocked.Decrement(ref active);
                },null,0,1);
                Assert.True(started.Wait(3000));
                var disposing=Task.Run(() => timer.Dispose());
                Assert.NotSame(disposing, await Task.WhenAny(disposing, Task.Delay(30)));
                release.Set(); Assert.Same(disposing, await Task.WhenAny(disposing, Task.Delay(3000)));
                await disposing;
                int stopped=calls; Thread.Sleep(30);
                Assert.Equal(stopped,calls); Assert.Equal(0,overlap); Assert.Equal(0,active);
            }
        }

        [Fact]
        public void PendingIssueSurvivesRestartAndResumesSameNumber()
        {
            var repo=Repository();
            repo.Insert(new BatchRecord { BatchDate="26188",BatchNo=7,ProductName="P1",QrGrade="A",IssueTime=new DateTime(2026,7,7) });
            var reopened=new BatchRepository(new DataContext(Config()));
            Assert.Equal(7,reopened.GetPendingAck(4002).BatchNo);
            var plc=new FakePlcClient(); var snapshot=new FakeSnapshot();
            snapshot.SetDInt(4002,1);
            using(var service=new BatchService(plc,snapshot,reopened,new BusinessConfig(),()=>new DateTime(2026,7,7)))
            {
                service.InitFromDatabase(); service.EnableForTest(); service.TickOnce();
                Assert.Contains("D4100=00007",plc.StringWrites);
                Assert.Equal(8,service.CurrentBatchNo);
                Assert.Null(reopened.GetPendingAck(4002));
                Assert.Single(reopened.Query("26188","26188",null));
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void PendingUploadResendsSuccessWithoutOverwriting(bool withstand)
        {
            var repo=Repository(); repo.Insert(new BatchRecord { BatchDate="26188", BatchNo=1, ProductName="P1",IssueTime=DateTime.Now });
            if(withstand) repo.UpdateWithstand(new WithstandTestRecord { BatchDate="26188",BatchNo=1,Voltage=220,Resistance=10,Current=1,Result=1 });
            else repo.UpdatePressure(new PressureTestRecord { BatchDate="26188",BatchNo=1,Pressure=2,Result=1 });
            var reopened=new BatchRepository(new DataContext(Config())); var plc=new FakePlcClient();
            if(withstand)
            {
                using(var service=new WithstandService(plc,new FakeSnapshot(),reopened,new BusinessConfig()))
                    Assert.True(service.ProcessUploadForTest("26188",1,"P1","999","999","999",2));
                Assert.Equal(220,reopened.Query("26188","26188",null)[0].Voltage);
            }
            else
            {
                using(var service=new PressureService(plc,new FakeSnapshot(),reopened,new BusinessConfig()))
                    Assert.True(service.ProcessUploadForTest("26188",1,"P1","999",2));
                Assert.Equal(2,reopened.Query("26188","26188",null)[0].Pressure);
            }
            int channel=withstand?4010:4020;
            Assert.Contains(plc.Writes,w=>w.Address==channel&&w.Value==2);
            Assert.Null(reopened.GetPendingAck(channel));
        }

        [Fact]
        public void BackupIsRestorableAndDoesNotDeleteFiveYearOldRows()
        {
            var repo=Repository();
            repo.Insert(new BatchRecord { BatchDate="21001",BatchNo=1,ProductName="OLD",IssueTime=new DateTime(2021,1,1) });
            using(var backup=new DatabaseBackupService(new DataContext(Config()),Config()))
            {
                string saved=backup.BackupNow();
                var restored=new BatchRepository(new DataContext(new DbConfig {FilePath=saved}));
                Assert.Equal("OLD",restored.Query("21001","26365",null).Single().ProductName);
                Assert.True(repo.Exists("21001",1));
                Assert.True(File.Exists(saved));
            }
        }

        [Fact]
        public void PagingAndExportCoverAllRowsWithoutDuplicates()
        {
            var repo=Repository(); var context=new DataContext(Config());
            using(var conn=context.OpenConnection())
            using(var transaction=conn.BeginTransaction())
            using(var cmd=conn.CreateCommand())
            {
                cmd.Transaction=transaction;
                cmd.CommandText="INSERT INTO batch_record(batch_date,batch_no,product_name) VALUES('26188',@n,'P1')";
                cmd.Parameters.Add(new SQLiteParameter("@n"));
                for(int i=1;i<=2505;i++){cmd.Parameters[0].Value=i;cmd.ExecuteNonQuery();}
                transaction.Commit();
            }
            Assert.Equal(500,repo.QueryPage("26188","26188",null,0,500).Count);
            Assert.Equal(5,repo.QueryPage("26188","26188",null,2500,500).Count);
            var rows=repo.ExportRows("26188","26188",null).ToList();
            Assert.Equal(2505,rows.Count); Assert.Equal(2505,rows.Select(r=>r.Id).Distinct().Count());
            Assert.Equal(2505,rows.First().BatchNo); Assert.Equal(1,rows.Last().BatchNo);
        }

        [Fact]
        public void FullSyncAndDiskSpaceGateAreEnabled()
        {
            Repository(); var cfg=Config(); var context=new DataContext(cfg);
            Assert.Equal(2,Convert.ToInt32(context.ExecuteScalar("PRAGMA synchronous")));
            Assert.True(context.TestConnection());
            cfg.MinimumFreeSpaceMb=int.MaxValue;
            Assert.False(context.TestConnection());
        }

        [Fact]
        public void UnacknowledgedChannelCannotBeOverwrittenByAnotherBatch()
        {
            var repo = Repository();
            repo.Insert(new BatchRecord { BatchDate="26188", BatchNo=1, IssueTime=DateTime.Now });
            Assert.Throws<InvalidOperationException>(() => repo.Insert(new BatchRecord { BatchDate="26188", BatchNo=2, IssueTime=DateTime.Now }));
            Assert.False(repo.Exists("26188",2));
            Assert.Equal(1,repo.GetPendingAck(4002).BatchNo);
        }

        [Fact]
        public async Task ConcurrentWritesReadsAndOnlineBackupRemainConsistent()
        {
            Repository();
            var context = new DataContext(Config());
            var tasks = Enumerable.Range(0, 3).Select(worker => Task.Run(() =>
            {
                for (int i = 1; i <= 100; i++)
                {
                    context.ExecuteNonQuery("INSERT INTO batch_record(batch_date,batch_no,product_name) VALUES('26188',@n,'P1')",
                        DataContext.Param("@n", worker * 100 + i));
                    context.ExecuteScalar("SELECT COUNT(*) FROM batch_record");
                }
            })).ToList();
            tasks.Add(Task.Run(() =>
            {
                using (var backup = new DatabaseBackupService(context, Config()))
                {
                    string saved = backup.BackupNow();
                    var restored = new DataContext(new DbConfig { FilePath = saved });
                    Assert.Equal("ok", Convert.ToString(restored.ExecuteScalar("PRAGMA integrity_check")));
                }
            }));
            await Task.WhenAll(tasks);
            Assert.Equal(300L, Convert.ToInt64(context.ExecuteScalar("SELECT COUNT(*) FROM batch_record")));
            Assert.Equal("ok", Convert.ToString(context.ExecuteScalar("PRAGMA integrity_check")));
        }

        [Fact]
        public void FailedPollFieldDoesNotReusePreviousEmployee()
        {
            var client = new FakePlcClient(); client.SetDInt(4030, 600249);
            using (var plc = new PlcService(new PlcConfig { ReadIntervalMs = 60000 }, client))
            {
                plc.Start(); plc.PollOnce();
                Assert.True(plc.TryGetDInt(4030, out int employee)); Assert.Equal(600249, employee);
                client.BeforeReadRegisters = address => { if (address == 4030) throw new IOException("read fault"); };
                plc.PollOnce(); Assert.False(plc.TryGetDInt(4030, out _));
                client.Disconnect(); Assert.False(plc.TryGetDInt(4002, out _));
            }
        }

        [Fact]
        public void ChangedFlagDuringPollIsNotPublished()
        {
            var client = new FakePlcClient(); client.SetDInt(4002, 1);
            int reads = 0;
            client.BeforeReadRegisters = address =>
            {
                Assert.NotEqual(4000, address);
                if (address == 4002 && ++reads == 2) client.SetDInt(4002, 0);
            };
            using (var plc = new PlcService(new PlcConfig { ReadIntervalMs = 60000 }, client))
            { plc.Start(); plc.PollOnce(); Assert.False(plc.TryGetDInt(4002, out _)); }
        }

        [Fact]
        public void ReconnectDuringPollInvalidatesWholeCycle()
        {
            var client = new FakePlcClient(); client.SetDInt(4002, 1);
            bool reconnected = false;
            client.BeforeReadRegisters = address =>
            {
                if (address == 5000 && !reconnected) { reconnected = true; client.Disconnect(); client.Connect(); }
            };
            using (var plc = new PlcService(new PlcConfig { ReadIntervalMs = 60000 }, client))
            { plc.Start(); plc.PollOnce(); Assert.False(plc.TryGetDInt(4002, out _)); }
        }

        [Fact]
        public void EngineIncludesWalResetFix()
        {
            Repository();
            var version = Version.Parse(Convert.ToString(new DataContext(Config()).ExecuteScalar("SELECT sqlite_version()")));
            Assert.True(version >= new Version(3, 53, 3), "Deployment must include the tested native SQLite engine: " + version);
        }

        [Fact]
        public void InvalidIntervalsAreRejected()
        {
            var cfg=new AppConfig(); cfg.Plc.ReadIntervalMs=0;
            Assert.Throws<ArgumentException>(()=>cfg.Validate());
        }

        public void Dispose()
        {
            SQLiteConnection.ClearAllPools();
            try { if(Directory.Exists(_dir)) Directory.Delete(_dir,true); } catch(IOException) { }
        }
    }
}
