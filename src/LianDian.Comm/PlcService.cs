using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using LianDian.Core.Config;
using LianDian.Core.Logging;
using log4net;

namespace LianDian.Comm
{
    /// <summary>
    /// PLC 生命周期与快照轮询：启动即连、串行轮询、同周期发布；失败字段和过期快照不进入业务。
    /// 心跳位 D4000 绝不进入任何读操作。
    /// </summary>
    public sealed class PlcService : IDisposable, IPlcSnapshotReader, IPlcSnapshotSource
    {
        private static readonly ILog Log = LogHelper.Get(LogHelper.Communication);

        private readonly PlcConfig _config;
        private readonly IPlcClient _client;
        private readonly object _snapshotSync = new object();
        private PlcSnapshot _published;
        private int _polling;
        private int _connectionGeneration;
        private readonly LianDian.Core.SerialTimer _reconnectTimer;
        private readonly LianDian.Core.SerialTimer _pollTimer;
        private volatile bool _running;

        public PlcService(PlcConfig config)
            : this(config, new ModbusTcpPlcClient(config))
        {
        }

        public PlcService(PlcConfig config, IPlcClient client)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _client.ConnectionChanged += OnClientConnectionChanged;
            _reconnectTimer = new LianDian.Core.SerialTimer(_ => TryConnect(), null, Timeout.Infinite, Timeout.Infinite);
            _pollTimer = new LianDian.Core.SerialTimer(_ => PollOnce(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public bool IsConnected => _client.IsConnected;

        /// <summary>供业务层执行写操作。</summary>
        public IPlcClient Client => _client;

        public event EventHandler ConnectionChanged;

        public void Start()
        {
            _running = true;
            TryConnect();
            _pollTimer.Change(Math.Max(50, _config.ReadIntervalMs), _config.ReadIntervalMs);
        }

        public void Stop()
        {
            _running = false;
            _pollTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _reconnectTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        public void TryConnect()
        {
            if (!_running || _client.IsConnected) return;
            try
            {
                _client.Connect();
            }
            catch (Exception ex)
            {
                Log.WarnFormat("PLC 连接失败：{0}（{1}ms 后重试）", ex.Message, _config.ReconnectIntervalMs);
                _reconnectTimer.Change(_config.ReconnectIntervalMs, _config.ReconnectIntervalMs);
            }
        }

        private void OnClientConnectionChanged(object sender, EventArgs e)
        {
            lock (_snapshotSync)
            {
                _connectionGeneration++;
                _published = null;
            }
            if (_client.IsConnected)
            {
                _reconnectTimer.Change(Timeout.Infinite, Timeout.Infinite);
                Log.Info("PLC 连接恢复，停止重连定时器");
            }
            else if (_running)
            {
                // 运行中掉线：自动武装重连定时器（覆盖初次连接成功后的掉线）
                _reconnectTimer.Change(_config.ReconnectIntervalMs, _config.ReconnectIntervalMs);
                Log.InfoFormat("PLC 掉线，{0}ms 后自动重连", _config.ReconnectIntervalMs);
            }
            ConnectionChanged?.Invoke(this, e);
        }

        /// <summary>逐块读取，二次确认握手标志，再原子发布完整周期；失败字段不保留旧值。</summary>
        public void PollOnce()
        {
            if (!_running || !_client.IsConnected) return;
            if (Interlocked.Exchange(ref _polling, 1) != 0) return;
            try
            {
            int started = Environment.TickCount;
            int generation;
            lock (_snapshotSync) generation = _connectionGeneration;
            var cycle = new Dictionary<int, ushort[]>();
            foreach (PollBlock block in RegisterMap.PollBlocks)
            {
                if (!_running || !_client.IsConnected) break;
                try
                {
                    int count = block.Type == BlockType.String ? RegisterMap.StringCount(block.StartAddress, _config) : block.Count;
                    ushort[] regs = _client.ReadRegisters(block.StartAddress, count);
                    cycle[block.StartAddress] = regs;
                }
                catch (Exception ex)
                {
                    Log.WarnFormat("轮询块 D{0}({1} 寄存器) 失败，本周期不提供该点数据：{2}", block.StartAddress, block.Count, ex.Message);
                }
            }
            // PLC 必须先写数据再置 1；两次标志一致才发布，避免把新标志和上一批字段拼接。
            foreach (int flagAddress in new[] { RegisterMap.D4002_BatchIssueFlag, RegisterMap.D4010_WithstandFlag, RegisterMap.D4020_PressureFlag })
            {
                if (!_running || !_client.IsConnected) break;
                try
                {
                    var confirm = _client.ReadRegisters(flagAddress, 2);
                    if (!cycle.TryGetValue(flagAddress, out var before) || !before.SequenceEqual(confirm)) cycle.Remove(flagAddress);
                }
                catch { cycle.Remove(flagAddress); }
            }
            lock (_snapshotSync)
                _published = generation == _connectionGeneration && _running && _client.IsConnected && unchecked((uint)(Environment.TickCount - started)) <= _config.SnapshotMaxAgeMs
                    ? new PlcSnapshot(cycle, _config) : null;
            }
            finally { Interlocked.Exchange(ref _polling, 0); }
        }

        public IPlcSnapshotReader Capture()
        {
            lock (_snapshotSync)
                return (_running && _client.IsConnected ? _published : null) ??
                    new PlcSnapshot(new Dictionary<int, ushort[]>(), _config);
        }

        public bool TryGetDInt(int dAddress, out int value)
        {
            return Capture().TryGetDInt(dAddress, out value);
        }

        public bool TryGetWord(int dAddress, out ushort value)
        {
            return Capture().TryGetWord(dAddress, out value);
        }

        public bool TryGetString(int dAddress, out string value)
        {
            return Capture().TryGetString(dAddress, out value);
        }

        public void Dispose()
        {
            Stop();
            _reconnectTimer.Dispose();
            _pollTimer.Dispose();
            _client.ConnectionChanged -= OnClientConnectionChanged;
            _client.Dispose();
        }
    }
}
