using System;
using System.IO;
using LianDian.Business.Services;
using LianDian.Comm;
using LianDian.Core;
using LianDian.Core.Config;
using LianDian.Core.Logging;
using LianDian.Data;
using log4net;

namespace LianDian.Business
{
    /// <summary>全系统装配：日志 → 自动建表 → 健康监控 → PLC → 各业务服务。Start/Dispose 统一生命周期。</summary>
    public sealed class SystemManager : IDisposable
    {
        private static readonly ILog Log = LogHelper.Get(LogHelper.System);
        private readonly AppConfig _config;
        private bool _started;
        private bool _disposed;

        private SystemManager(AppConfig config)
        {
            _config = config;
        }

        public PlcService Plc { get; private set; }
        public DatabaseHealthMonitor DbHealth { get; private set; }
        public HeartbeatService Heartbeat { get; private set; }
        public BatchService Batch { get; private set; }
        public WithstandService Withstand { get; private set; }
        public PressureService Pressure { get; private set; }
        public IBatchRepository BatchRepo { get; private set; }
        public DatabaseInitializer Initializer { get; private set; }
        public DatabaseBackupService Backups { get; private set; }
        public AppConfig Config => _config;

        public static SystemManager Create(AppConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            config.Validate();

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string logDir = PathResolver.Resolve(baseDir, config.Paths.LogDir, createIfMissing: true);
            LogHelper.Configure(logDir);

            var manager = new SystemManager(config);
            manager.Initializer = new DatabaseInitializer(config.Db);
            manager.Initializer.EnsureCreated();

            var context = new DataContext(config.Db);
            Log.InfoFormat("数据库路径={0}；SQLite={1}；WAL/FULL；历史记录不自动删除", context.FilePath,
                context.ExecuteScalar("SELECT sqlite_version()"));
            manager.BatchRepo = new BatchRepository(context);
            manager.DbHealth = new DatabaseHealthMonitor(context, config.Db.TestIntervalSec);
            manager.Backups = new DatabaseBackupService(context, config.Db);
            manager.Plc = new PlcService(config.Plc);

            manager.Heartbeat = new HeartbeatService(manager.Plc.Client, () => manager.DbHealth.IsHealthy, config.Business);
            manager.Batch = new BatchService(manager.Plc.Client, manager.Plc, manager.BatchRepo, config.Business);
            manager.Withstand = new WithstandService(manager.Plc.Client, manager.Plc, manager.BatchRepo, config.Business);
            manager.Pressure = new PressureService(manager.Plc.Client, manager.Plc, manager.BatchRepo, config.Business);
            return manager;
        }

        public void Start()
        {
            if (_started) return;
            _started = true;
            Log.Info("=== 系统启动 ===");
            DbHealth.Start();
            Backups.Start();
            Plc.Start();
            Heartbeat.Start();
            Batch.Start();
            Withstand.Start();
            Pressure.Start();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { Backups?.Dispose(); } catch (Exception ex) { Log.WarnFormat("Backup Dispose 异常：{0}", ex.Message); }
            try { Heartbeat?.Dispose(); } catch (Exception ex) { Log.WarnFormat("Heartbeat Dispose 异常：{0}", ex.Message); }
            try { Pressure?.Dispose(); } catch (Exception ex) { Log.WarnFormat("Pressure Dispose 异常：{0}", ex.Message); }
            try { Withstand?.Dispose(); } catch (Exception ex) { Log.WarnFormat("Withstand Dispose 异常：{0}", ex.Message); }
            try { Batch?.Dispose(); } catch (Exception ex) { Log.WarnFormat("Batch Dispose 异常：{0}", ex.Message); }
            try { Plc?.Dispose(); } catch (Exception ex) { Log.WarnFormat("Plc Dispose 异常：{0}", ex.Message); }
            try { DbHealth?.Dispose(); } catch (Exception ex) { Log.WarnFormat("DbHealth Dispose 异常：{0}", ex.Message); }
            Log.Info("=== 系统已释放 ===");
        }
    }
}
