using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using LianDian.Core.Config;
using LianDian.Core.Logging;
using log4net;
using NModbus;

namespace LianDian.Comm
{
    /// <summary>
    /// NModbus TCP 封装：内部锁串行化、读写超时、IO 异常自动标记断开并触发事件。
    /// </summary>
    public sealed class ModbusTcpPlcClient : IPlcClient
    {
        private static readonly ILog Log = LogHelper.Get(LogHelper.Communication);

        private readonly PlcConfig _config;
        private readonly object _sync = new object();
        private TcpClient _tcpClient;
        private IModbusMaster _master;
        private bool _disposed;

        public ModbusTcpPlcClient(PlcConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public bool IsConnected { get; private set; }

        public event EventHandler ConnectionChanged;

        public void Connect()
        {
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(ModbusTcpPlcClient));
                if (IsConnected) return;
                TcpClient pending = null;
                try
                {
                    var tcp = new TcpClient();
                    pending = tcp;
                    IAsyncResult ar = tcp.BeginConnect(_config.Ip, _config.Port, null, null);
                    using (var wait = ar.AsyncWaitHandle)
                    {
                        if (!wait.WaitOne(Math.Max(500, _config.TimeOutMs)))
                            throw new TimeoutException("PLC TCP 连接超时");
                    }
                    tcp.EndConnect(ar);

                    tcp.NoDelay = true;
                    tcp.ReceiveTimeout = _config.TimeOutMs;
                    tcp.SendTimeout = _config.TimeOutMs;

                    var factory = new ModbusFactory();
                    IModbusMaster master = factory.CreateMaster(tcp);
                    master.Transport.ReadTimeout = _config.TimeOutMs;
                    master.Transport.WriteTimeout = _config.TimeOutMs;
                    master.Transport.Retries = 1;

                    _tcpClient = tcp;
                    _master = master;
                    pending = null;
                    IsConnected = true;
                    Log.InfoFormat("PLC 已连接 {0}:{1} (SlaveId={2}, Offset={3})", _config.Ip, _config.Port, _config.SlaveId, _config.ModbusOffset);
                    RaiseConnectionChanged();
                }
                catch
                {
                    pending?.Close();
                    CleanupInternal();
                    IsConnected = false;
                    RaiseConnectionChanged();
                    throw;
                }
            }
        }

        public void Disconnect()
        {
            lock (_sync)
            {
                CleanupInternal();
                if (IsConnected)
                {
                    IsConnected = false;
                    Log.Info("PLC 已断开");
                    RaiseConnectionChanged();
                }
            }
        }

        public ushort[] ReadRegisters(int dAddress, int count)
        {
            return Execute(() => _master.ReadHoldingRegisters(_config.SlaveId, RegisterMap.ToModbus(dAddress, _config.ModbusOffset), (ushort)count));
        }

        public int ReadDInt(int dAddress)
        {
            ushort[] regs = ReadRegisters(dAddress, 2);
            return ModbusCodec.ToDInt(regs, 0, _config.WordOrder);
        }

        public void WriteDInt(int dAddress, int value)
        {
            WriteRegisters(dAddress, ModbusCodec.FromDInt(value, _config.WordOrder));
        }

        public void WriteString(int dAddress, string value)
        {
            int count = RegisterMap.StringCount(dAddress, _config);
            Encoding encoding = ResolveEncoding(_config.StringEncoding);
            if (encoding.GetByteCount(value ?? string.Empty) >= count * 2)
                throw new ArgumentException("字符串超出寄存器容量，必须保留结束符。", nameof(value));
            WriteRegisters(dAddress, ModbusCodec.EncodeString(value, count, encoding, _config.StringLowByteFirst));
        }

        private void WriteRegisters(int dAddress, ushort[] registers)
        {
            Exception lastError = null;
            int attempts = Math.Max(1, _config.WriteRetryCount);
            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    if (!IsConnected) Connect();
                    Execute(() =>
                    {
                        _master.WriteMultipleRegisters(_config.SlaveId,
                            RegisterMap.ToModbus(dAddress, _config.ModbusOffset), registers);
                        return 0;
                    });
                    if (attempt > 1)
                        Log.InfoFormat("PLC D{0} 第 {1} 次写入成功", dAddress, attempt);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    Log.WarnFormat("PLC D{0} 第 {1}/{2} 次写入失败：{3}", dAddress, attempt, attempts, ex.Message);
                    if (attempt < attempts && _config.WriteRetryDelayMs > 0)
                        Thread.Sleep(_config.WriteRetryDelayMs);
                }
            }
            throw new IOException(string.Format("PLC D{0} 连续 {1} 次写入失败", dAddress, attempts), lastError);
        }

        public string ReadString(int dAddress, int registerCount)
        {
            if (RegisterMap.StringRegisterCounts.ContainsKey(dAddress))
                registerCount = RegisterMap.StringCount(dAddress, _config);
            ushort[] regs = ReadRegisters(dAddress, registerCount);
            return ModbusCodec.DecodeString(regs, 0, registerCount, ResolveEncoding(_config.StringEncoding), _config.StringLowByteFirst);
        }

        public void Dispose()
        {
            lock (_sync) { _disposed = true; Disconnect(); }
        }

        private T Execute<T>(Func<T> action)
        {
            lock (_sync)
            {
                if (!IsConnected || _master == null)
                    throw new IOException("PLC 未连接");
                try
                {
                    return action();
                }
                catch (Exception ex)
                {
                    MarkDisconnected(ex);
                    throw;
                }
            }
        }

        private void MarkDisconnected(Exception ex)
        {
            Log.WarnFormat("PLC 通讯异常，标记断开：{0}", ex.Message);
            CleanupInternal();
            IsConnected = false;
            RaiseConnectionChanged();
        }

        private void CleanupInternal()
        {
            var master = _master;
            _master = null;
            try { master?.Dispose(); } catch { /* ignore */ }

            var tcp = _tcpClient;
            _tcpClient = null;
            try { tcp?.Close(); } catch { /* ignore */ }
        }

        private void RaiseConnectionChanged()
        {
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public static Encoding ResolveEncoding(string name)
        {
            if (string.IsNullOrEmpty(name)) return Encoding.UTF8;
            if (name.Equals("GBK", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("GB2312", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("936", StringComparison.OrdinalIgnoreCase))
                return Encoding.GetEncoding(936);
            return Encoding.UTF8;
        }
    }
}
