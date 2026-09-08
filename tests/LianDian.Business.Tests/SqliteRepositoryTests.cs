using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using LianDian.Core.Config;
using LianDian.Core.Models;
using LianDian.Data;
using Xunit;

namespace LianDian.Business.Tests
{
    public sealed class SqliteRepositoryTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "LianDianTests", Guid.NewGuid().ToString("N"));

        [Fact]
        public void EnsureCreated_IsIdempotent_AndCreatesFile()
        {
            DbConfig config = Config();
            var initializer = new DatabaseInitializer(config);
            initializer.EnsureCreated();
            initializer.EnsureCreated();

            Assert.True(File.Exists(config.FilePath));
            Assert.True(new DataContext(config).TestConnection());
        }

        [Fact]
        public void Repository_RoundTripsBatchAndTestData()
        {
            DbConfig config = Config();
            new DatabaseInitializer(config).EnsureCreated();
            var repository = new BatchRepository(new DataContext(config));

            long id = repository.Insert(new BatchRecord
            {
                BatchDate = "26239", BatchNo = 7, EmployeeNo = "1001", QrGrade = "C",
                ProductName = "HT11-ACBARBUS", IssueTime = new DateTime(2026, 8, 27, 9, 30, 0)
            });
            repository.UpdateWithstand(new WithstandTestRecord
            {
                BatchDate = "26239", BatchNo = 7, Voltage = 220.5m,
                Resistance = 10.2m, Current = 0.8m, Result = 1
            });
            repository.UpdatePressure(new PressureTestRecord
            {
                BatchDate = "26239", BatchNo = 7, Pressure = 0.5m, Result = 2
            });

            BatchRecord row = repository.Query("26239", "26239", "HT11").Single();
            Assert.True(id > 0);
            Assert.Equal("C", row.QrGrade);
            Assert.Equal(220.5m, row.Voltage);
            Assert.Equal(0.5m, row.Pressure);
            Assert.Equal((short)1, row.WithstandResult);
            Assert.Equal((short)2, row.PressureResult);
        }

        [Fact]
        public void Repository_TranslatesUniqueConstraint()
        {
            DbConfig config = Config();
            new DatabaseInitializer(config).EnsureCreated();
            var repository = new BatchRepository(new DataContext(config));
            var row = new BatchRecord { BatchDate = "26239", BatchNo = 1, IssueTime = DateTime.Now };

            repository.Insert(row);

            Assert.Throws<DuplicateKeyException>(() => repository.Insert(row));
        }

        private DbConfig Config()
        {
            Directory.CreateDirectory(_directory);
            return new DbConfig { FilePath = Path.Combine(_directory, "test.db"), TestIntervalSec = 1 };
        }

        public void Dispose()
        {
            SQLiteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
            }
            catch (IOException)
            {
                // System.Data.SQLite 的本机连接池可能延迟释放文件句柄；临时目录由系统后续清理。
            }
        }
    }
}
