using System;
using System.Threading;
using LianDian.Core.Logging;
using log4net;

namespace LianDian.Data
{
    /// <summary>周期可写性和磁盘余量监控，供心跳门控与 UI 状态灯使用。</summary>
    public sealed class DatabaseHealthMonitor : IDisposable
    {
        private static readonly ILog Log = LogHelper.Get(LogHelper.Database);
        private readonly DataContext _context;
        private readonly LianDian.Core.SerialTimer _timer;
        private volatile bool _isHealthy;

        public DatabaseHealthMonitor(DataContext context, int testIntervalSec)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _timer = new LianDian.Core.SerialTimer(_ => CheckOnce(), null, Timeout.Infinite, Timeout.Infinite);
            TestInterval = TimeSpan.FromSeconds(Math.Max(1, testIntervalSec));
        }

        public TimeSpan TestInterval { get; }

        public bool IsHealthy => _isHealthy;

        /// <summary>健康状态变化事件（true/false 翻转时触发）。</summary>
        public event EventHandler HealthChanged;

        public void Start()
        {
            CheckOnce();
            _timer.Change(TestInterval, TestInterval);
        }

        public void Stop()
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        public void CheckOnce()
        {
            bool healthy = _context.TestConnection();
            if (healthy != _isHealthy)
            {
                _isHealthy = healthy;
                Log.InfoFormat("数据库健康状态变化：{0}", healthy ? "正常" : "异常");
                HealthChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            Stop();
            _timer.Dispose();
        }
    }
}
