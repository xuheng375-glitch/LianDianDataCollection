using System;
using System.Threading;
using LianDian.Comm;
using LianDian.Core;
using LianDian.Core.Config;
using LianDian.Core.Logging;
using log4net;

namespace LianDian.Business.Services
{
    /// <summary>
    /// 心跳发送：周期写 D4000 0/1 反转（纯写入，绝不读取）。
    /// 门控：PLC 或数据库任一掉线 → 停止发送；恢复后自动复发。
    /// </summary>
    public sealed class HeartbeatService : IDisposable
    {
        private static readonly ILog Log = LogHelper.Get(LogHelper.Business);
        private readonly IPlcClient _plc;
        private readonly Func<bool> _dbHealthy;
        private readonly BusinessConfig _config;
        private readonly Func<DateTime> _now;
        private readonly LianDian.Core.SerialTimer _timer;
        private volatile bool _running;
        private int _value;
        private HeartbeatState _state = HeartbeatState.Stopped;

        public HeartbeatService(IPlcClient plc, Func<bool> dbHealthy, BusinessConfig config)
            : this(plc, dbHealthy, config, () => DateTime.Now)
        {
        }

        public HeartbeatService(IPlcClient plc, Func<bool> dbHealthy, BusinessConfig config, Func<DateTime> now)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _dbHealthy = dbHealthy ?? throw new ArgumentNullException(nameof(dbHealthy));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _now = now ?? throw new ArgumentNullException(nameof(now));
            _timer = new LianDian.Core.SerialTimer(_ => TickOnce(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public HeartbeatState State => _state;

        /// <summary>心跳状态变化事件（Stopped/Running）。</summary>
        public event EventHandler<HeartbeatState> StateChanged;

        public void Start()
        {
            _running = true;
            _timer.Change(Math.Max(50, _config.HeartbeatIntervalMs), _config.HeartbeatIntervalMs);
        }

        public void Stop()
        {
            _running = false;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>单次心跳周期（测试钩子：TickOnce）。</summary>
        public void TickOnce()
        {
            if (!_running) return;
            try
            {
                if (_plc.IsConnected && _dbHealthy())
                {
                    _value = 1 - _value;
                    _plc.WriteDInt(RegisterMap.D4000_Heartbeat, _value);
                    SetState(HeartbeatState.Running);
                }
                else
                {
                    SetState(HeartbeatState.Stopped);
                }
            }
            catch (Exception ex)
            {
                Log.WarnFormat("心跳写入失败：{0}", ex.Message);
                SetState(HeartbeatState.Stopped);
            }
        }

        /// <summary>测试钩子：不启动定时器，仅置运行态。</summary>
        internal void EnableForTest()
        {
            _running = true;
        }

        private void SetState(HeartbeatState newState)
        {
            if (_state == newState) return;
            _state = newState;
            Log.InfoFormat("心跳状态：{0}", newState);
            StateChanged?.Invoke(this, newState);
        }

        public void Dispose()
        {
            Stop();
            _timer.Dispose();
        }
    }
}
