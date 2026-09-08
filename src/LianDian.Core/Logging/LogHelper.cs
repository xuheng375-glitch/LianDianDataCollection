using System;
using System.IO;
using System.Text;
using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Layout;
using log4net.Repository.Hierarchy;

namespace LianDian.Core.Logging
{
    /// <summary>
    /// log4net 程序化配置（无 XML Configure），按大小滚动文件，分类日志名称。
    /// </summary>
    public static class LogHelper
    {
        public const string Communication = "Communication";
        public const string Business = "Business";
        public const string Database = "Database";
        public const string UI = "UI";
        public const string System = "System";

        private static readonly object Sync = new object();
        private static volatile bool _configured;

        /// <summary>配置根 Logger：滚动文件 + 控制台可选。幂等，重复调用仅更新目录与级别。</summary>
        public static void Configure(string logDir, string level = "DEBUG")
        {
            if (string.IsNullOrEmpty(logDir)) throw new ArgumentNullException(nameof(logDir));
            lock (Sync)
            {
                Directory.CreateDirectory(logDir);
                var hierarchy = (Hierarchy)LogManager.GetRepository();
                hierarchy.Root.RemoveAllAppenders();

                var layout = new PatternLayout("%date{yyyy-MM-dd HH:mm:ss.fff} [%thread] %-5level %logger - %message%newline");
                layout.ActivateOptions();

                var appender = new RollingFileAppender
                {
                    File = Path.Combine(logDir, "app.log"),
                    AppendToFile = true,
                    RollingStyle = RollingFileAppender.RollingMode.Size,
                    DatePattern = ".yyyyMMdd",
                    StaticLogFileName = true,
                    MaxSizeRollBackups = 30,
                    MaximumFileSize = "20MB",
                    Encoding = Encoding.UTF8,
                    Layout = layout,
                    LockingModel = new FileAppender.MinimalLock()
                };
                appender.ActivateOptions();

                hierarchy.Root.AddAppender(appender);
                hierarchy.Root.Level = ParseLevel(level);
                hierarchy.Configured = true;
                _configured = true;
            }
        }

        public static ILog Get(string category)
        {
            return LogManager.GetLogger(category ?? System);
        }

        public static bool IsConfigured => _configured;

        private static Level ParseLevel(string level)
        {
            if (string.IsNullOrEmpty(level)) return Level.Debug;
            switch (level.Trim().ToUpperInvariant())
            {
                case "ALL": return Level.All;
                case "DEBUG": return Level.Debug;
                case "INFO": return Level.Info;
                case "WARN": return Level.Warn;
                case "ERROR": return Level.Error;
                case "FATAL": return Level.Fatal;
                case "OFF": return Level.Off;
                default: return Level.Debug;
            }
        }
    }
}
