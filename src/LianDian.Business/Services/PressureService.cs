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
    /// 气压上传：D4020=1 → 读数据 → 三方比对 → 历史已存在标志位=3；否则 UPDATE 同批记录标志位=2。
    /// </summary>
    public sealed class PressureService : IDisposable
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

        public PressureService(IPlcClient plc, IPlcSnapshotReader snapshot, IBatchRepository repo, BusinessConfig config)
            : this(plc, snapshot, repo, config, () => DateTime.Now)
        {
        }

        public PressureService(IPlcClient plc, IPlcSnapshotReader snapshot, IBatchRepository repo, BusinessConfig config, Func<DateTime> now)
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
            if (!Snapshot.TryGetDInt(RegisterMap.D4020_PressureFlag, out flag)) return;
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
                Log.ErrorFormat("气压上传异常：{0}", ex);
                ErrorOccurred?.Invoke(this, new UploadErrorEventArgs("气压上传异常：" + ex.Message));
                return false;
            }
        }

        /// <summary>核心上传流程（测试钩子：ProcessUploadForTest 直接执行核心链路）。</summary>
        internal bool ProcessUploadForTest(string batchDate, int batchNo, string productName, string pressure, int result)
        {
            return ProcessUploadCore(batchDate, batchNo, productName, pressure.SafeToDecimal(), result);
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
            if (!Snapshot.TryGetString(RegisterMap.D5900_PressureDate, out date) || !PlcText.IsDate(date)) return false;
            if (!Snapshot.TryGetString(RegisterMap.D4300_PressureBatchNo, out batchText) || !PlcText.TryBatch(batchText, out batchNo)) return false;
            if (!Snapshot.TryGetDInt(RegisterMap.D4024_PressureResult, out result)) return false;

            string productName = null;
            string product;
            if (Snapshot.TryGetString(RegisterMap.D5200_PressureProductName, out product))
                productName = product;

            string pressure = ReadString(RegisterMap.D5600_PressureValue);
            return ProcessUploadCore(date, batchNo, productName, pressure.SafeToDecimal(), result);
        }

        private bool ProcessUploadCore(string batchDate, int batchNo, string productName, decimal? pressure, int result)
        {
            var payload = new PressureTestRecord
            {
                BatchDate = batchDate,
                BatchNo = batchNo,
                ProductName = productName,
                Pressure = pressure,
                Result = result,
                UploadTime = _now()
            };

            if (!_repo.ExistsByProduct(batchDate, batchNo, productName))
            {
                Log.WarnFormat("气压数据比对不一致：{0}-{1} 产品={2}，保持标志位=1", batchDate, batchNo, productName);
                DataMismatch?.Invoke(this, new DataMismatchEventArgs("气压数据与批次记录不一致", payload));
                return false;
            }

            var store = _repo as IPendingPlcAckStore;
            var pending = store?.GetPendingAck(RegisterMap.D4020_PressureFlag);
            if (pending != null && pending.BatchDate == batchDate && pending.BatchNo == batchNo)
            {
                _plc.WriteDInt(RegisterMap.D4020_PressureFlag, (int)FlagState.Success);
                store.ClearPendingAck(RegisterMap.D4020_PressureFlag, batchDate, batchNo);
                RecordUploaded?.Invoke(this, new UploadEventArgs(payload, FlagState.Success));
                return true;
            }
            if (_repo.HasPressureData(batchDate, batchNo))
            {
                _plc.WriteDInt(RegisterMap.D4020_PressureFlag, (int)FlagState.Error);
                Log.WarnFormat("气压历史数据已存在：{0}-{1}，标志位=3", batchDate, batchNo);
                RecordUploaded?.Invoke(this, new UploadEventArgs(payload, FlagState.Error));
                return true;
            }

            if (string.IsNullOrWhiteSpace(productName) || !pressure.HasValue || (result != 1 && result != 2)) return false;
            int affected = _repo.UpdatePressure(payload);
            if (affected <= 0)
            {
                Log.WarnFormat("气压 UPDATE 未命中记录：{0}-{1}", batchDate, batchNo);
                return false;
            }
            _plc.WriteDInt(RegisterMap.D4020_PressureFlag, (int)FlagState.Success);
            store?.ClearPendingAck(RegisterMap.D4020_PressureFlag, batchDate, batchNo);
            Log.InfoFormat("气压上传成功：{0}-{1} 气压={2} 结果={3}", batchDate, batchNo, pressure, result);
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
}
