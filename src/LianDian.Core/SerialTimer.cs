using System;
using System.Threading;
using LianDian.Core.Logging;

namespace LianDian.Core
{
    /// <summary>不排队、不重入；释放等待正在执行的回调完成，异常不会逃逸线程池。</summary>
    public sealed class SerialTimer : IDisposable
    {
        private readonly object _gate = new object();
        private readonly object _stateGate = new object();
        private readonly Timer _timer;
        private readonly TimerCallback _callback;
        private readonly object _state;
        private volatile bool _enabled;
        private volatile bool _disposed;
        public SerialTimer(TimerCallback callback, object state, int dueTime, int period)
        {
            _callback = callback;
            _state = state;
            _timer = new Timer(Run, null, Timeout.Infinite, Timeout.Infinite);
            Change(dueTime, period);
        }
        private void Run(object ignored)
        {
            if (!Monitor.TryEnter(_gate)) return;
            try
            {
                if (!_disposed && _enabled) _callback(_state);
            }
            catch (Exception ex) { LogHelper.Get(LogHelper.System).Error("定时任务异常，等待下一周期重试", ex); }
            finally { Monitor.Exit(_gate); }
        }
        public void Change(int dueTime, int period)
        {
            lock (_stateGate)
            {
                if (_disposed) return;
                _enabled = dueTime != Timeout.Infinite;
                _timer.Change(dueTime, period);
            }
        }
        public void Change(TimeSpan dueTime, TimeSpan period) => Change((int)dueTime.TotalMilliseconds, (int)period.TotalMilliseconds);
        public void Dispose()
        {
            lock (_stateGate)
            {
                if (_disposed) return;
                _disposed = true;
                _enabled = false;
                _timer.Dispose();
            }
            lock (_gate) { } // 不持有状态锁等待回调，避免通信事件重新调度定时器时锁反转。
        }
    }
}
