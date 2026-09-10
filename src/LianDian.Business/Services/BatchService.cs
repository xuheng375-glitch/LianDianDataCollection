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
    /// 批次号管理：DB 当日最大批次+1 续号，D4002=1 写 D5700/D4100、读工号/产品名、
    /// 插入空测试字段记录、标志位=2；重复键幂等；可配时刻跨天重置为 1。
    /// </summary>
    public sealed class BatchService : IDisposable
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
        private DateTime _lastResetDate;
        private int _inFlight; // 不可重入保护：Timer 回调可能并发，防止同一标志位被处理两遍
        private int _lastFlag;       // 最近一次快照标志位（上升沿判定）
        private bool _retryPending;  // 上次处理失败，允许在标志位仍为 1 时重试

        public BatchService(IPlcClient plc, IPlcSnapshotReader snapshot, IBatchRepository repo, BusinessConfig config)
            : this(plc, snapshot, repo, config, () => DateTime.Now)
        {
        }

        public BatchService(IPlcClient plc, IPlcSnapshotReader snapshot, IBatchRepository repo, BusinessConfig config, Func<DateTime> now)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _now = now ?? throw new ArgumentNullException(nameof(now));
            _timer = new LianDian.Core.SerialTimer(_ => TickOnce(), null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>当前批次号（DB 续号，跨天重置为 1）。</summary>
        public int CurrentBatchNo { get; private set; }

        public event EventHandler BatchIssued;

        public event EventHandler<BatchErrorEventArgs> ErrorOccurred;

        public void Start()
        {
            _running = true;
            InitFromDatabase();
            _timer.Change(Math.Max(50, _config.PollIntervalMs), _config.PollIntervalMs);
        }

        public void Stop()
        {
            _running = false;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>启动续号：当日最大批次号 + 1。</summary>
        public void InitFromDatabase()
        {
            DateTime now = _now();
            string batchDate = now.ToBatchDateString();
            CurrentBatchNo = _repo.GetMaxBatchNo(batchDate) + 1;
            _lastResetDate = now.Date;
            Log.InfoFormat("批次号初始化：{0} 起始批次 {1}", batchDate, CurrentBatchNo);
        }

        /// <summary>单次轮询：跨天重置检查 + 标志位=1 时下发（测试钩子：TickOnce）。</summary>
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
            CheckDailyReset();
            int flag;
            if (!Snapshot.TryGetDInt(RegisterMap.D4002_BatchIssueFlag, out flag)) return;
            if (flag != (int)FlagState.Waiting)
            {
                PendingAckCoordinator.TryReconcileIdle(_repo, RegisterMap.D4002_BatchIssueFlag, flag, Log,
                    message => ErrorOccurred?.Invoke(this, new BatchErrorEventArgs(message)));
            }
            bool shouldProcess = flag == (int)FlagState.Waiting && (_lastFlag != (int)FlagState.Waiting || _retryPending);
            _lastFlag = flag;
            if (shouldProcess)
            {
                bool ok = ProcessIssueSafe();
                _retryPending = !ok;
            }
        }

        private bool ProcessIssueSafe()
        {
            try
            {
                ProcessIssue();
                return true;
            }
            catch (Exception ex)
            {
                Log.ErrorFormat("批次下发异常：{0}", ex);
                ErrorOccurred?.Invoke(this, new BatchErrorEventArgs("批次下发异常：" + ex.Message));
                return false;
            }
        }

        /// <summary>核心下发流程（测试钩子：ProcessIssueForTest 直接执行核心链路）。</summary>
        internal bool ProcessIssueForTest(string batchDate, int batchNo, string employeeNo, string productName)
        {
            return ProcessIssueCore(batchDate, batchNo, employeeNo, productName);
        }

        /// <summary>测试钩子：不启动定时器，仅置运行态。</summary>
        internal void EnableForTest()
        {
            _running = true;
        }

        private void ProcessIssue()
        {
            var store = _repo as IPendingPlcAckStore;
            var pending = store?.GetPendingAck(RegisterMap.D4002_BatchIssueFlag);
            if (pending != null)
            {
                _plc.WriteString(RegisterMap.D5700_BatchIssueDate, pending.BatchDate);
                _plc.WriteString(RegisterMap.D4100_BatchIssueNo, pending.BatchNo.ToString("D5", CultureInfo.InvariantCulture));
                _plc.WriteDInt(RegisterMap.D4002_BatchIssueFlag, (int)FlagState.Success);
                store.ClearPendingAck(RegisterMap.D4002_BatchIssueFlag, pending.BatchDate, pending.BatchNo);
                CurrentBatchNo = _repo.GetMaxBatchNo(_now().ToBatchDateString()) + 1;
                BatchIssued?.Invoke(this, EventArgs.Empty);
                return;
            }
            DateTime now = _now();
            string batchDate = now.ToBatchDateString();
            int batchNo = CurrentBatchNo;
            string employeeNo = ReadEmployeeNo();
            string productName = null;
            string product;
            if (Snapshot.TryGetString(RegisterMap.D5000_ProductName, out product))
                productName = product;
            if (employeeNo == null || string.IsNullOrWhiteSpace(productName))
                throw new InvalidOperationException("产品名称或员工工号尚未有效读取，暂停批次下发。");
            ProcessIssueCore(batchDate, batchNo, employeeNo, productName);
        }

        private bool ProcessIssueCore(string batchDate, int batchNo, string employeeNo, string productName)
        {
            if (string.IsNullOrWhiteSpace(productName) || productName.Trim() == "0")
                throw new InvalidOperationException("D5000产品名称不能为空或为0，暂停批次下发，保持D4002=1。");
            int employee;
            if (!int.TryParse(employeeNo, NumberStyles.Integer, CultureInfo.InvariantCulture, out employee) || employee == 0)
                throw new InvalidOperationException("D4030员工工号未有效读取或为0，暂停批次下发，保持D4002=1。");
            // 写批次下发日期/批次号
            if (batchNo < 1 || batchNo > 99999) throw new InvalidOperationException("批次号必须为00001～99999。");
            if (!PlcText.IsDate(batchDate)) throw new InvalidOperationException("日期必须为有效的yyDDD编码。");
            _plc.WriteString(RegisterMap.D5700_BatchIssueDate, batchDate);
            _plc.WriteString(RegisterMap.D4100_BatchIssueNo, batchNo.ToString("D5", CultureInfo.InvariantCulture));

            var record = new BatchRecord
            {
                BatchDate = batchDate,
                BatchNo = batchNo,
                EmployeeNo = employeeNo,
                ProductName = productName,
                IssueTime = _now()
            };
            try
            {
                _repo.Insert(record);
            }
            catch (DuplicateKeyException ex)
            {
                Log.WarnFormat("批次 {0}-{1} 已存在，幂等处理：{2}", batchDate, batchNo, ex.Message);
                _plc.WriteDInt(RegisterMap.D4002_BatchIssueFlag, (int)FlagState.Success);
                (_repo as IPendingPlcAckStore)?.ClearPendingAck(RegisterMap.D4002_BatchIssueFlag, batchDate, batchNo);
                CurrentBatchNo = Math.Max(batchNo + 1, _repo.GetMaxBatchNo(batchDate) + 1);
                BatchIssued?.Invoke(this, EventArgs.Empty);
                return true;
            }

            _plc.WriteDInt(RegisterMap.D4002_BatchIssueFlag, (int)FlagState.Success);
            (_repo as IPendingPlcAckStore)?.ClearPendingAck(RegisterMap.D4002_BatchIssueFlag, batchDate, batchNo);
            CurrentBatchNo = batchNo + 1;
            Log.InfoFormat("批次下发成功：{0}-{1} 产品={2} 工号={3}", batchDate, batchNo, productName, employeeNo);
            BatchIssued?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private string ReadEmployeeNo()
        {
            int employeeNo;
            return Snapshot.TryGetDInt(RegisterMap.D4030_EmployeeNo, out employeeNo)
                ? employeeNo.ToString(CultureInfo.InvariantCulture)
                : null;
        }

        private void CheckDailyReset()
        {
            DateTime now = _now();
            if (now.Date > _lastResetDate && now.TimeOfDay >= _config.BatchResetTime)
            {
                CurrentBatchNo = _repo.GetMaxBatchNo(now.ToBatchDateString()) + 1;
                _lastResetDate = now.Date;
                Log.InfoFormat("跨天批次续号：{0} 下一批 {1}", now.ToBatchDateString(), CurrentBatchNo);
            }
        }

        public void Dispose()
        {
            Stop();
            _timer.Dispose();
        }
    }

    public sealed class BatchErrorEventArgs : EventArgs
    {
        public BatchErrorEventArgs(string message)
        {
            Message = message;
        }

        public string Message { get; }
    }
}
