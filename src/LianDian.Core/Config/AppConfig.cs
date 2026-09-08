using System;

namespace LianDian.Core.Config
{
    public sealed class PlcConfig
    {
        public string Ip { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 502;
        public byte SlaveId { get; set; } = 1;
        public int ReadIntervalMs { get; set; } = 200;
        public int TimeOutMs { get; set; } = 1000;
        public int ReconnectIntervalMs { get; set; } = 3000;
        public int WriteRetryCount { get; set; } = 3;
        public int WriteRetryDelayMs { get; set; } = 200;
        public bool WordOrder { get; set; } = true;
        public int ModbusOffset { get; set; } = 1;
        public string StringEncoding { get; set; } = "UTF8";
        public bool StringLowByteFirst { get; set; } = true;
        public int BatchStringRegisters { get; set; } = 3;
        public int DateStringRegisters { get; set; } = 3;
        public int QrGradeStringRegisters { get; set; } = 1;
        public int SnapshotMaxAgeMs { get; set; } = 3000;
    }

    public sealed class DbConfig
    {
        public string FilePath { get; set; } = "Data\\lian_dian.db";
        public int TestIntervalSec { get; set; } = 5;
        public string BackupDirectory { get; set; } = "Data\\Backups";
        public int BackupIntervalHours { get; set; } = 24;
        public int BackupRetentionDays { get; set; } = 7;
        public int MinimumFreeSpaceMb { get; set; } = 1024;
    }

    public sealed class PathConfig
    {
        public string ProductPicDir { get; set; } = "ProductPIC";
        public string InstructionDir { get; set; } = "Instruction";
        public string LogDir { get; set; } = "Logs";
    }

    public sealed class BusinessConfig
    {
        public TimeSpan BatchResetTime { get; set; } = TimeSpan.Zero;
        public int HeartbeatIntervalMs { get; set; } = 500;
        public int PollIntervalMs { get; set; } = 300;
    }

    /// <summary>
    /// 聚合全部 INI 配置节，缺省值对齐 config/app.ini 示例。
    /// </summary>
    public sealed class AppConfig
    {
        public PlcConfig Plc { get; } = new PlcConfig();
        public DbConfig Db { get; } = new DbConfig();
        public PathConfig Paths { get; } = new PathConfig();
        public BusinessConfig Business { get; } = new BusinessConfig();

        public static AppConfig Load(string iniPath)
        {
            if (iniPath == null) throw new ArgumentNullException(nameof(iniPath));
            var ini = new IniFile(iniPath);
            var cfg = new AppConfig();

            cfg.Plc.Ip = ini.Get("PLC", "Ip", cfg.Plc.Ip);
            cfg.Plc.Port = ini.GetInt("PLC", "Port", cfg.Plc.Port);
            int slaveId = ini.GetInt("PLC", "SlaveId", cfg.Plc.SlaveId);
            if (slaveId < 1 || slaveId > 247) throw new ArgumentException("PLC SlaveId 必须为 1～247。");
            cfg.Plc.SlaveId = (byte)slaveId;
            cfg.Plc.ReadIntervalMs = ini.GetInt("PLC", "ReadIntervalMs", cfg.Plc.ReadIntervalMs);
            cfg.Plc.TimeOutMs = ini.GetInt("PLC", "TimeOutMs", cfg.Plc.TimeOutMs);
            cfg.Plc.ReconnectIntervalMs = ini.GetInt("PLC", "ReconnectIntervalMs", cfg.Plc.ReconnectIntervalMs);
            cfg.Plc.WriteRetryCount = Math.Max(1, ini.GetInt("PLC", "WriteRetryCount", cfg.Plc.WriteRetryCount));
            cfg.Plc.WriteRetryDelayMs = Math.Max(0, ini.GetInt("PLC", "WriteRetryDelayMs", cfg.Plc.WriteRetryDelayMs));
            cfg.Plc.WordOrder = ini.GetBool("PLC", "WordOrder", cfg.Plc.WordOrder);
            cfg.Plc.ModbusOffset = ini.GetInt("PLC", "ModbusOffset", cfg.Plc.ModbusOffset);
            cfg.Plc.StringEncoding = ini.Get("PLC", "StringEncoding", cfg.Plc.StringEncoding);
            cfg.Plc.StringLowByteFirst = ini.GetBool("PLC", "StringLowByteFirst", cfg.Plc.StringLowByteFirst);
            cfg.Plc.BatchStringRegisters = ini.GetInt("PLC", "BatchStringRegisters", 3);
            cfg.Plc.DateStringRegisters = ini.GetInt("PLC", "DateStringRegisters", 3);
            cfg.Plc.QrGradeStringRegisters = ini.GetInt("PLC", "QrGradeStringRegisters", 1);
            cfg.Plc.SnapshotMaxAgeMs = ini.GetInt("PLC", "SnapshotMaxAgeMs", 3000);
            if (cfg.Plc.BatchStringRegisters < 3 || cfg.Plc.BatchStringRegisters > 99 ||
                cfg.Plc.DateStringRegisters < 3 || cfg.Plc.DateStringRegisters > 99 ||
                cfg.Plc.QrGradeStringRegisters < 1 || cfg.Plc.QrGradeStringRegisters > 99)
                throw new ArgumentException("PLC 字符串寄存器长度无效：批次/日期为3～99，等级为1～99。");

            cfg.Db.FilePath = ini.Get("Database", "FilePath", cfg.Db.FilePath);
            cfg.Db.TestIntervalSec = ini.GetInt("Database", "TestIntervalSec", cfg.Db.TestIntervalSec);
            cfg.Db.BackupDirectory = ini.Get("Database", "BackupDirectory", cfg.Db.BackupDirectory);
            cfg.Db.BackupIntervalHours = ini.GetInt("Database", "BackupIntervalHours", 24);
            cfg.Db.BackupRetentionDays = ini.GetInt("Database", "BackupRetentionDays", 7);
            cfg.Db.MinimumFreeSpaceMb = ini.GetInt("Database", "MinimumFreeSpaceMb", 1024);

            cfg.Paths.ProductPicDir = ini.Get("Path", "ProductPicDir", cfg.Paths.ProductPicDir);
            cfg.Paths.InstructionDir = ini.Get("Path", "InstructionDir", cfg.Paths.InstructionDir);
            cfg.Paths.LogDir = ini.Get("Path", "LogDir", cfg.Paths.LogDir);

            cfg.Business.BatchResetTime = ini.GetTimeSpan("Business", "BatchResetTime", cfg.Business.BatchResetTime);
            cfg.Business.HeartbeatIntervalMs = ini.GetInt("Business", "HeartbeatIntervalMs", cfg.Business.HeartbeatIntervalMs);
            cfg.Business.PollIntervalMs = ini.GetInt("Business", "PollIntervalMs", cfg.Business.PollIntervalMs);

            cfg.Validate();
            return cfg;
        }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Plc.Ip) || Plc.Port < 1 || Plc.Port > 65535 || Plc.SlaveId == 0 || Plc.SlaveId > 247 ||
                Plc.ReadIntervalMs < 50 || Plc.ReadIntervalMs > 60000 || Plc.TimeOutMs < 100 || Plc.TimeOutMs > 10000 ||
                Plc.ReconnectIntervalMs < 100 || Plc.ReconnectIntervalMs > 60000 || Plc.SnapshotMaxAgeMs < 100 || Plc.SnapshotMaxAgeMs > 60000 ||
                Plc.WriteRetryCount < 1 || Plc.WriteRetryCount > 5 || Plc.WriteRetryDelayMs < 0 || Plc.WriteRetryDelayMs > 5000 ||
                Plc.ModbusOffset < -3999 || Plc.ModbusOffset > 59000 ||
                Plc.BatchStringRegisters < 3 || Plc.BatchStringRegisters > 99 ||
                Plc.DateStringRegisters < 3 || Plc.DateStringRegisters > 99 ||
                Plc.QrGradeStringRegisters < 1 || Plc.QrGradeStringRegisters > 99 ||
                Business.PollIntervalMs < 50 || Business.PollIntervalMs > 60000 || Business.HeartbeatIntervalMs < 50 || Business.HeartbeatIntervalMs > 60000 ||
                Business.BatchResetTime < TimeSpan.Zero || Business.BatchResetTime >= TimeSpan.FromDays(1) ||
                Db.TestIntervalSec < 1 || Db.TestIntervalSec > 3600 || Db.BackupIntervalHours < 1 || Db.BackupIntervalHours > 168 ||
                Db.BackupRetentionDays < 2 || Db.BackupRetentionDays > 3650 || Db.MinimumFreeSpaceMb < 1 ||
                string.IsNullOrWhiteSpace(Db.FilePath) || string.IsNullOrWhiteSpace(Db.BackupDirectory))
                throw new ArgumentException("运行配置超出安全范围，请检查 PLC、定时周期和数据库配置。");
        }
    }
}
