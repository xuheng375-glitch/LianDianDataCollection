using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using LianDian.Business;
using LianDian.Core.Config;
using LianDian.Core.Logging;
using LianDian.UI.Theme;
using LianDian.UI.Forms;
using Sunny.UI;

namespace LianDian.UI
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            using (var instance = new Mutex(true, "Local\\LianDianDataCollection-PLC", out bool first))
            {
                if (!first)
                {
                    using (var notice = new ExitConfirmForm("程序已在运行", "请使用现有窗口，避免多个实例重复采集。", false)) notice.ShowDialog();
                    return;
                }
                try { RunApplication(); }
                finally { instance.ReleaseMutex(); }
            }
        }

        private static void RunApplication()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            UIStyles.InitColorful(FlatTheme.Cyan, FlatTheme.Text);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
                LogHelper.Get(LogHelper.UI).Error("UI 线程未处理异常", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                LogHelper.Get(LogHelper.System).Fatal("进程级未处理异常", e.ExceptionObject as Exception);

            string iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "app.ini");
            try
            {
                if (!File.Exists(iniPath)) CreateDefaultConfig(iniPath);
                var config = AppConfig.Load(iniPath);
                using (SystemManager manager = SystemManager.Create(config))
                {
                    manager.Start();
                    Application.Run(new MainForm(manager));
                }
            }
            catch (Exception ex)
            {
                LogHelper.Get(LogHelper.System).Fatal("系统启动失败", ex);
                using (var notice = new ExitConfirmForm("系统启动失败", "请检查数据库、PLC 配置和日志。\r\n" + ex.Message, false)) notice.ShowDialog();
            }
        }

        private static void CreateDefaultConfig(string iniPath)
        {
            const string content = @"[PLC]
Ip=127.0.0.1
Port=502
SlaveId=1
ReadIntervalMs=200
TimeOutMs=1000
ReconnectIntervalMs=3000
WriteRetryCount=3
WriteRetryDelayMs=200
WordOrder=true
ModbusOffset=1
StringEncoding=UTF8
StringLowByteFirst=true
BatchStringRegisters=3
DateStringRegisters=3
QrGradeStringRegisters=1
SnapshotMaxAgeMs=3000

[Database]
FilePath=Data\lian_dian.db
TestIntervalSec=5
BackupDirectory=Data\Backups
BackupIntervalHours=24
BackupRetentionDays=7
MinimumFreeSpaceMb=1024

[Path]
ProductPicDir=ProductPIC
InstructionDir=Instruction
LogDir=Logs

[Business]
BatchResetTime=00:00:00
HeartbeatIntervalMs=500
PollIntervalMs=300
";
            string dir = Path.GetDirectoryName(iniPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(iniPath, content);
        }
    }
}
