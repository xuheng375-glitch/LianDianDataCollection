using System;
using System.Data.SQLite;
using System.IO;
using System.Threading;
using LianDian.Core;
using LianDian.Core.Config;
using LianDian.Core.Logging;

namespace LianDian.Data
{
    /// <summary>在线一致性备份。主库数据永久保留；只轮换本服务生成的完整备份文件。</summary>
    public sealed class DatabaseBackupService : IDisposable
    {
        private readonly DataContext _context;
        private readonly DbConfig _config;
        private readonly SerialTimer _timer;
        private DateTime _lastSuccess = DateTime.MinValue;
        private volatile bool _stopping;
        public string LastError { get; private set; }
        public DateTime LastSuccess => _lastSuccess;
        public DatabaseBackupService(DataContext context, DbConfig config)
        {
            _context = context; _config = config;
            _timer = new SerialTimer(_ => CheckOnce(), null, Timeout.Infinite, Timeout.Infinite);
        }
        public void Start() => _timer.Change(60000, 60000);
        private void CheckOnce()
        {
            if (DateTime.UtcNow - _lastSuccess < TimeSpan.FromHours(_config.BackupIntervalHours)) return;
            try { BackupNow(); LastError = null; }
            catch (Exception ex)
            {
                LastError = ex.Message;
                LogHelper.Get(LogHelper.Database).Error("数据库备份失败（采集数据仍保留在主库）", ex);
            }
        }
        public string BackupNow()
        {
            string dir = Path.GetFullPath(Path.IsPathRooted(_config.BackupDirectory) ? _config.BackupDirectory :
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _config.BackupDirectory));
            Directory.CreateDirectory(dir);
            long required = new FileInfo(_context.FilePath).Length + (long)_config.MinimumFreeSpaceMb * 1024 * 1024;
            // WAL 中尚未 checkpoint 的页也占用备份容量；按完整 WAL 计入作为保守余量。
            string wal = _context.FilePath + "-wal";
            if (File.Exists(wal)) required += new FileInfo(wal).Length;
            if (new DriveInfo(Path.GetPathRoot(dir)).AvailableFreeSpace < required)
                throw new IOException("备份磁盘剩余空间不足，未开始备份。");
            string target = Path.Combine(dir, "lian-dian-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".db");
            string pending = target + ".partial";
            try
            {
                using (var source = _context.OpenConnection())
                using (var destination = new SQLiteConnection(new SQLiteConnectionStringBuilder
                { DataSource = pending, Pooling = false, SyncMode = SynchronizationModes.Full }.ToString()))
                {
                    destination.Open();
                    DateTime deadline = DateTime.UtcNow.AddMinutes(30);
                    source.BackupDatabase(destination, "main", "main", 256,
                        (s, a, d, b, pages, remaining, total, retry) =>
                        {
                            if (_stopping || DateTime.UtcNow > deadline) throw new OperationCanceledException("备份已取消或超时。");
                            return true;
                        }, 10);
                    using (var command = destination.CreateCommand())
                    {
                        command.CommandText = "PRAGMA quick_check";
                        if (!string.Equals(Convert.ToString(command.ExecuteScalar()), "ok", StringComparison.OrdinalIgnoreCase))
                            throw new IOException("备份完整性检查失败。");
                    }
                }
                File.Move(pending, target);
                _lastSuccess = DateTime.UtcNow;
                LastError = null;
                try
                {
                // 仅识别本服务自己的 GUID 格式，不清理主库或人工备份。
                foreach (string file in Directory.GetFiles(dir, "lian-dian-backup-*.db"))
                {
                    try
                    {
                        string name = Path.GetFileNameWithoutExtension(file);
                        if (name.Length < 32 || !Guid.TryParseExact(name.Substring(name.Length - 32), "N", out _)) continue;
                        if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-_config.BackupRetentionDays)) File.Delete(file);
                    }
                    catch (Exception cleanupError)
                    {
                        LogHelper.Get(LogHelper.Database).Warn("旧备份清理失败，不影响本次备份成功：" + file, cleanupError);
                    }
                }
                }
                catch (Exception cleanupError)
                {
                    LogHelper.Get(LogHelper.Database).Warn("备份目录枚举失败，不影响本次备份成功：" + dir, cleanupError);
                }
                LogHelper.Get(LogHelper.Database).Info("在线备份已完成并校验：" + target);
                return target;
            }
            catch
            {
                try { if (File.Exists(pending)) File.Delete(pending); }
                catch (Exception cleanupError) { LogHelper.Get(LogHelper.Database).Warn("未完成备份临时文件清理失败：" + pending, cleanupError); }
                throw;
            }
        }
        public void Dispose() { _stopping = true; _timer.Dispose(); }
    }
}
