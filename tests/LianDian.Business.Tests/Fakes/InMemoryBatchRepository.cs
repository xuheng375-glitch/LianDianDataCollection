using System;
using System.Collections.Generic;
using System.Linq;
using LianDian.Core.Models;
using LianDian.Data;

namespace LianDian.Business.Tests.Fakes
{
    /// <summary>内存版单表仓储：唯一键冲突抛 DuplicateKeyException，行为对齐 SQLite 实现。</summary>
    public sealed class InMemoryBatchRepository : IBatchRepository, IPendingPlcAckStore
    {
        private readonly List<BatchRecord> _records = new List<BatchRecord>();
        private readonly Dictionary<int, PendingPlcAck> _pending = new Dictionary<int, PendingPlcAck>();
        private long _nextId = 1;

        public IList<BatchRecord> Records => _records;
        public bool FailNextClearAck { get; set; }
        public long CountUnboundWithstand() => _records.LongCount(r => !r.WithstandResult.HasValue);

        public int GetMaxBatchNo(string batchDate)
        {
            return _records
                .Where(r => string.Equals(r.BatchDate, batchDate, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.BatchNo)
                .DefaultIfEmpty(0)
                .Max();
        }

        public IList<BatchRecord> QueryPage(string fromDate, string toDate, string productName, int offset, int limit)
            => Query(fromDate, toDate, productName).Skip(offset).Take(limit).ToList();
        public IEnumerable<BatchRecord> ExportRows(string fromDate, string toDate, string productName)
            => Query(fromDate, toDate, productName);

        public bool Exists(string batchDate, int batchNo)
        {
            return _records.Any(r => r.BatchDate == batchDate && r.BatchNo == batchNo);
        }

        public bool ExistsByProduct(string batchDate, int batchNo, string productName)
        {
            return _records.Any(r => r.BatchDate == batchDate && r.BatchNo == batchNo &&
                                     string.Equals(r.ProductName, productName, StringComparison.Ordinal));
        }

        public bool HasWithstandData(string batchDate, int batchNo)
        {
            BatchRecord r = Find(batchDate, batchNo);
            return r != null && r.WithstandResult.HasValue;
        }

        public bool HasPressureData(string batchDate, int batchNo)
        {
            BatchRecord r = Find(batchDate, batchNo);
            return r != null && r.PressureResult.HasValue;
        }

        public long Insert(BatchRecord record)
        {
            if (Exists(record.BatchDate, record.BatchNo))
                throw new DuplicateKeyException("批次已存在：" + record.BatchDate + "-" + record.BatchNo);
            EnsureChannelAvailable(4002, record.BatchDate, record.BatchNo);
            var copy = Clone(record);
            copy.Id = _nextId++;
            _records.Add(copy);
            SavePending(4002, record.BatchDate, record.BatchNo);
            return copy.Id;
        }

        public int BindQrGrade(string batchDate, int batchNo, string productName, string grade)
        {
            if (string.IsNullOrWhiteSpace(productName))
                throw new ArgumentException("二维码上传产品名称不能为空。", nameof(productName));
            if (grade == null || grade.Length != 1 || grade[0] < 'A' || grade[0] > 'F')
                throw new ArgumentException("二维码等级必须为A～F。", nameof(grade));
            var record = Find(batchDate, batchNo);
            if (record == null || !string.Equals(record.ProductName, productName, StringComparison.Ordinal) ||
                (record.QrGrade != null && record.QrGrade != grade)) return 0;
            EnsureChannelAvailable(4004, batchDate, batchNo);
            record.QrGrade = grade;
            SavePending(4004, batchDate, batchNo);
            return 1;
        }

        public int UpdateWithstand(WithstandTestRecord record)
        {
            BatchRecord r = Find(record.BatchDate, record.BatchNo);
            if (r == null || r.WithstandResult.HasValue) return 0;
            EnsureChannelAvailable(4010, record.BatchDate, record.BatchNo);
            r.Voltage = record.Voltage;
            r.Resistance = record.Resistance;
            r.Current = record.Current;
            r.WithstandResult = (short)record.Result;
            r.WithstandTime = record.UploadTime;
            SavePending(4010, record.BatchDate, record.BatchNo);
            return 1;
        }

        public int UpdatePressure(PressureTestRecord record)
        {
            BatchRecord r = Find(record.BatchDate, record.BatchNo);
            if (r == null || r.PressureResult.HasValue) return 0;
            EnsureChannelAvailable(4020, record.BatchDate, record.BatchNo);
            r.Pressure = record.Pressure;
            r.PressureResult = (short)record.Result;
            r.PressureTime = record.UploadTime;
            SavePending(4020, record.BatchDate, record.BatchNo);
            return 1;
        }

        public PendingPlcAck GetPendingAck(int channel)
        {
            PendingPlcAck pending;
            return _pending.TryGetValue(channel, out pending)
                ? new PendingPlcAck { Channel=pending.Channel, BatchDate=pending.BatchDate, BatchNo=pending.BatchNo }
                : null;
        }

        public void ClearPendingAck(int channel, string date, int batchNo)
        {
            if (FailNextClearAck) { FailNextClearAck = false; throw new InvalidOperationException("injected clear failure"); }
            PendingPlcAck pending;
            if (_pending.TryGetValue(channel, out pending) && pending.BatchDate == date && pending.BatchNo == batchNo)
                _pending.Remove(channel);
        }

        private void EnsureChannelAvailable(int channel, string date, int batchNo)
        {
            PendingPlcAck pending;
            if (_pending.TryGetValue(channel, out pending) && (pending.BatchDate != date || pending.BatchNo != batchNo))
                throw new InvalidOperationException("该 PLC 通道仍有上一批未确认应答。");
        }

        private void SavePending(int channel, string date, int batchNo)
        {
            _pending[channel] = new PendingPlcAck { Channel=channel, BatchDate=date, BatchNo=batchNo };
        }

        public IList<BatchRecord> Query(string fromDate, string toDate, string productName)
        {
            return _records
                .Where(r => string.CompareOrdinal(r.BatchDate, fromDate) >= 0 &&
                            string.CompareOrdinal(r.BatchDate, toDate) <= 0 &&
                            (string.IsNullOrEmpty(productName) ||
                             (r.ProductName ?? string.Empty).IndexOf(productName, StringComparison.Ordinal) >= 0))
                .OrderByDescending(r => r.BatchDate)
                .ThenByDescending(r => r.BatchNo)
                .Select(Clone)
                .ToList();
        }

        public IList<string> GetDistinctProductNames()
        {
            return _records
                .Where(r => !string.IsNullOrEmpty(r.ProductName))
                .Select(r => r.ProductName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
        }

        private BatchRecord Find(string batchDate, int batchNo)
        {
            return _records.FirstOrDefault(r => r.BatchDate == batchDate && r.BatchNo == batchNo);
        }

        private static BatchRecord Clone(BatchRecord r)
        {
            return new BatchRecord
            {
                Id = r.Id,
                BatchDate = r.BatchDate,
                BatchNo = r.BatchNo,
                EmployeeNo = r.EmployeeNo,
                ProductName = r.ProductName,
                QrGrade = r.QrGrade,
                IssueTime = r.IssueTime,
                Voltage = r.Voltage,
                Resistance = r.Resistance,
                Current = r.Current,
                WithstandResult = r.WithstandResult,
                WithstandTime = r.WithstandTime,
                Pressure = r.Pressure,
                PressureResult = r.PressureResult,
                PressureTime = r.PressureTime
            };
        }
    }
}
