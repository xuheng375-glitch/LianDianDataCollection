using System;
using System.Globalization;
using System.Threading;
using LianDian.Comm;
using LianDian.Core;
using LianDian.Core.Config;
using LianDian.Core.Logging;
using LianDian.Core.Models;
using LianDian.Data;
using log4net;

namespace LianDian.Business.Services
{
    /// <summary>
    /// 耐压上传：D4010=1 → 读数据 → 三方比对（产品+日期+批次）→
    /// 历史数据已存在标志位=3；否则 UPDATE 同批次记录填数标志位=2；比对不一致保持 1。
    /// </summary>
    public sealed class WithstandService : IDisposable
    {
        private static readonly ILog Log = LogHelper.Get(LogHelper.Business);
        private readonly IPlcClient _plc;
        private readonly IPlcSnapshotReader _snapshot;
        private IPlcSnapshotReader _cycleSnapshot;
        private IPlcSnapshotReader Snapshot => _cycleSnapshot ?? _snapshot;
        private readonly IBatchRepository _repo;
        private readonly BusinessConfig _config;
        private readonly Func<DateTime> _now;
        private readonly LianDian.Core.SerialTimer _timer;
        private volatile bool _running;
        private int _inFlight;
        private int _lastFlag;
        private bool _retryPending;

        public WithstandService(IPlcClient plc, IPlcSnapshotReader snapshot, IBatchRepository repo, BusinessConfig config)
            : this(plc, snapshot, repo, config, () => DateTime.Now)
        {
        }

        public WithstandService(IPlcClient plc, IPlcSnapshotReader snapshot, IBatchRepository repo, BusinessConfig config, Func<DateTime> now)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _now = now ?? throw new ArgumentNullException(nameof(now));
            _timer = new LianDian.Core.SerialTimer(_ => TickOnce(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public event EventHandler<UploadEventArgs> RecordUploaded;

        public event EventHandler<UploadErrorEventArgs> ErrorOccurred;

        public event EventHandler<DataMismatchEventArgs> DataMismatch;

        public void Start()
        {
            _running = true;
            _timer.Change(Math.Max(50, _config.PollIntervalMs), _config.PollIntervalMs);
        }

        public void Stop()
        {
            _running = false;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>单次轮询：标志位=1 时处理（测试钩子：TickOnce）。</summary>
        public void TickOnce()
        {
            if (!_running) return;
            if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0) return;
            try
            {
                TickCore();
            }
            finally
            {
                Interlocked.Exchange(ref _inFlight, 0);
            }
        }

        private void TickCore()
        {
            _cycleSnapshot = (_snapshot as IPlcSnapshotSource)?.Capture() ?? _snapshot;
            int flag;
            if (!Snapshot.TryGetDInt(RegisterMap.D4010_WithstandFlag, out flag)) return;
            bool shouldProcess = flag == (int)FlagState.Waiting && (_lastFlag != (int)FlagState.Waiting || _retryPending);
            _lastFlag = flag;
            if (shouldProcess)
            {
                bool ok = ProcessUploadSafe();
                _retryPending = !ok;
            }
        }

        private bool ProcessUploadSafe()
        {
            try
            {
                return ProcessUpload();
            }
            catch (Exception ex)
            {
                Log.ErrorFormat("耐压上传异常：{0}", ex);
                ErrorOccurred?.Invoke(this, new UploadErrorEventArgs("耐压上传异常：" + ex.Message));
                return false;
            }
        }

        /// <summary>核心上传流程（测试钩子：ProcessUploadForTest 直接执行核心链路）。</summary>
        internal bool ProcessUploadForTest(string batchDate, int batchNo, string productName, string voltage,
            string resistance, string current, int result)
        {
            return ProcessUploadCore(batchDate, batchNo, productName,
                voltage.SafeToDecimal(), resistance.SafeToDecimal(), current.SafeToDecimal(), result);
        }

        /// <summary>测试钩子：不启动定时器，仅置运行态。</summary>
        internal void EnableForTest()
        {
            _running = true;
        }

        private bool ProcessUpload()
        {
            string date;
            string batchText;
            int batchNo;
            int result;
            if (!Snapshot.TryGetString(RegisterMap.D5800_WithstandDate, out date) || !PlcText.IsDate(date)) return false;
            if (!Snapshot.TryGetString(RegisterMap.D4200_WithstandBatchNo, out batchText) || !PlcText.TryBatch(batchText, out batchNo)) return false;
            if (!Snapshot.TryGetDInt(RegisterMap.D4014_WithstandResult, out result)) return false;

            string productName = null;
            string product;
            if (Snapshot.TryGetString(RegisterMap.D5100_WithstandProductName, out product))
                productName = product;

            string voltage = ReadString(RegisterMap.D5300_Voltage);
            string resistance = ReadString(RegisterMap.D5400_Resistance);
            string current = ReadString(RegisterMap.D5500_Current);

            return ProcessUploadCore(date, batchNo, productName,
                voltage.SafeToDecimal(), resistance.SafeToDecimal(), current.SafeToDecimal(), result);
        }

        private bool ProcessUploadCore(string batchDate, int batchNo, string productName,
            decimal? voltage, decimal? resistance, decimal? current, int result)
        {
            var payload = new WithstandTestRecord
            {
                BatchDate = batchDate,
                BatchNo = batchNo,
                ProductName = productName,
                Voltage = voltage,
                Resistance = resistance,
                Current = current,
                Result = result,
                UploadTime = _now()
            };

            // 三方比对：产品 + 日期 + 批次与 batch_record 一致
            if (!_repo.ExistsByProduct(batchDate, batchNo, productName))
            {
                Log.WarnFormat("耐压数据比对不一致：{0}-{1} 产品={2}，保持标志位=1", batchDate, batchNo, productName);
                DataMismatch?.Invoke(this, new DataMismatchEventArgs("耐压数据与批次记录不一致", payload));
                return false;
            }

            // 历史数据检测：该批次耐压结果已非空 → 标志位=3
            var store = _repo as IPendingPlcAckStore;
            var pending = store?.GetPendingAck(RegisterMap.D4010_WithstandFlag);
            if (pending != null && pending.BatchDate == batchDate && pending.BatchNo == batchNo)
            {
                _plc.WriteDInt(RegisterMap.D4010_WithstandFlag, (int)FlagState.Success);
                store.ClearPendingAck(RegisterMap.D4010_WithstandFlag, batchDate, batchNo);
                RecordUploaded?.Invoke(this, new UploadEventArgs(payload, FlagState.Success));
                return true;
            }
            if (_repo.HasWithstandData(batchDate, batchNo))
            {
                _plc.WriteDInt(RegisterMap.D4010_WithstandFlag, (int)FlagState.Error);
                Log.WarnFormat("耐压历史数据已存在：{0}-{1}，标志位=3", batchDate, batchNo);
                RecordUploaded?.Invoke(this, new UploadEventArgs(payload, FlagState.Error));
                return true;
            }

            if (string.IsNullOrWhiteSpace(productName) || !voltage.HasValue || !resistance.HasValue ||
                !current.HasValue || (result != 1 && result != 2)) return false;
            int affected = _repo.UpdateWithstand(payload);
            if (affected <= 0)
            {
                Log.WarnFormat("耐压 UPDATE 未命中记录：{0}-{1}", batchDate, batchNo);
                return false;
            }
            _plc.WriteDInt(RegisterMap.D4010_WithstandFlag, (int)FlagState.Success);
            store?.ClearPendingAck(RegisterMap.D4010_WithstandFlag, batchDate, batchNo);
            Log.InfoFormat("耐压上传成功：{0}-{1} 电压={2} 电阻={3} 电流={4} 结果={5}", batchDate, batchNo, voltage, resistance, current, result);
            RecordUploaded?.Invoke(this, new UploadEventArgs(payload, FlagState.Success));
            return true;
        }

        private string ReadString(int dAddress)
        {
            string value;
            return Snapshot.TryGetString(dAddress, out value) ? value : null;
        }

        public void Dispose()
        {
            Stop();
            _timer.Dispose();
        }
    }

    public sealed class UploadEventArgs : EventArgs
    {
        public UploadEventArgs(object payload, FlagState state)
        {
            Payload = payload;
            State = state;
        }

        public object Payload { get; }
        public FlagState State { get; }
    }

    public sealed class UploadErrorEventArgs : EventArgs
    {
        public UploadErrorEventArgs(string message)
        {
            Message = message;
        }

        public string Message { get; }
    }

    public sealed class DataMismatchEventArgs : EventArgs
    {
        public DataMismatchEventArgs(string message, object payload)
        {
            Message = message;
            Payload = payload;
        }

        public string Message { get; }
        public object Payload { get; }
    }
}
