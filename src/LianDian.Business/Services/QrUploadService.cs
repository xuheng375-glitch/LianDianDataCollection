using System;
using System.Threading;
using LianDian.Comm;
using LianDian.Core;
using LianDian.Core.Config;
using LianDian.Core.Logging;
using LianDian.Data;
using log4net;

namespace LianDian.Business.Services
{
    /// <summary>D4004=1：按日期、批次号和产品名称绑定二维码等级，事务提交后回复2。</summary>
    public sealed class QrUploadService : IDisposable
    {
        private readonly RepeatAlarm _alarms = new RepeatAlarm();
        private static readonly ILog Log = LogHelper.Get(LogHelper.Business);
        private readonly IPlcClient _plc;
        private readonly IPlcSnapshotReader _snapshot;
        private readonly IBatchRepository _repo;
        private readonly BusinessConfig _config;
        private readonly PlcConfig _plcConfig;
        private readonly LianDian.Core.SerialTimer _timer;
        private volatile bool _running;
        private int _inFlight;
        private int _lastFlag;
        private bool _retryPending;

        public QrUploadService(IPlcClient plc, IPlcSnapshotReader snapshot, IBatchRepository repo,
            BusinessConfig config, PlcConfig plcConfig)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _plcConfig = plcConfig ?? throw new ArgumentNullException(nameof(plcConfig));
            _timer = new LianDian.Core.SerialTimer(_ => TickOnce(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public event EventHandler RecordUploaded;
        public event EventHandler<UploadErrorEventArgs> ErrorOccurred;

        public void Start()
        {
            _running = true;
            _timer.Change(Math.Max(50, _config.PollIntervalMs), _config.PollIntervalMs);
        }

        internal void EnableForTest() => _running = true;

        public void TickOnce()
        {
            if (!_running || Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0) return;
            try
            {
                var snapshot = (_snapshot as IPlcSnapshotSource)?.Capture() ?? _snapshot;
                int flag;
                if (!snapshot.TryGetDInt(RegisterMap.D4004_QrUploadFlag, out flag)) return;
                if (flag != 1)
                    PendingAckCoordinator.TryReconcileIdle(_repo, RegisterMap.D4004_QrUploadFlag, flag, Log, Report);
                bool process = flag == 1 && (_lastFlag != 1 || _retryPending);
                _lastFlag = flag;
                if (!process) return;
                _retryPending = true;
                ProcessUpload();
                if (_alarms.Reset()) Log.Info("二维码上传故障已恢复");
                _retryPending = false;
                RecordUploaded?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                if (_alarms.ShouldReport(ex.GetType().Name + ":" + ex.Message))
                {
                    Log.Error("二维码上传失败，等待重试", ex);
                    Report("二维码上传失败：" + ex.Message);
                }
            }
            finally { Interlocked.Exchange(ref _inFlight, 0); }
        }

        private void ProcessUpload()
        {
            // PLC 在应答前必须保持本次请求的数据不变。
            if (_plc.ReadDInt(RegisterMap.D4004_QrUploadFlag) != 1)
                throw new InvalidOperationException("二维码请求已变化，等待下一轮读取。");
            string date = _plc.ReadString(RegisterMap.D6100_QrDate, _plcConfig.DateStringRegisters);
            string batch = _plc.ReadString(RegisterMap.D4400_QrBatchNo, _plcConfig.BatchStringRegisters);
            string grade = _plc.ReadString(RegisterMap.D6000_QrGrade, _plcConfig.QrGradeStringRegisters);
            string productName = _plc.ReadString(RegisterMap.D6200_QrProductName,
                RegisterMap.StringCount(RegisterMap.D6200_QrProductName, _plcConfig));
            int batchNo;
            if (!PlcText.IsDate(date) || !PlcText.TryBatch(batch, out batchNo))
                throw new InvalidOperationException("D6100日期或D4400批次号无效（应为yyDDD和五位批次号）。");
            if (grade == null || grade.Length != 1 || grade[0] < 'A' || grade[0] > 'F')
                throw new InvalidOperationException("D6000二维码等级必须为A～F。");
            if (string.IsNullOrWhiteSpace(productName))
                throw new InvalidOperationException("D6200二维码上传产品名称不能为空。");
            if (_plc.ReadDInt(RegisterMap.D4004_QrUploadFlag) != 1)
                throw new InvalidOperationException("读取期间二维码上传标志发生变化。");
            if (_repo.BindQrGrade(date, batchNo, productName, grade) != 1)
                throw new InvalidOperationException("未找到与日期、批次号、产品名称一致的记录，或已有不同二维码等级。");
            _plc.WriteDInt(RegisterMap.D4004_QrUploadFlag, 2);
            (_repo as IPendingPlcAckStore)?.ClearPendingAck(RegisterMap.D4004_QrUploadFlag, date, batchNo);
            Log.InfoFormat("二维码上传成功：{0}-{1} 产品={2} 等级={3}", date, batch, productName, grade);
        }

        private void Report(string message) => ErrorOccurred?.Invoke(this, new UploadErrorEventArgs(message));

        public void Dispose()
        {
            _running = false;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            _timer.Dispose();
        }
    }
}
