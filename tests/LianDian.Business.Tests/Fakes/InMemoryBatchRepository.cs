using System;
using System.Collections.Generic;
using System.Linq;
using LianDian.Core.Models;
using LianDian.Data;

namespace LianDian.Business.Tests.Fakes
{
    /// <summary>内存版单表仓储：唯一键冲突抛 DuplicateKeyException，行为对齐 SQLite 实现。</summary>
    public sealed class InMemoryBatchRepository : IBatchRepository
    {
        private readonly List<BatchRecord> _records = new List<BatchRecord>();
        private long _nextId = 1;

        public IList<BatchRecord> Records => _records;

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
                                     string.Equals(r.ProductName, productName, StringComparison.OrdinalIgnoreCase));
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
            var copy = Clone(record);
            copy.Id = _nextId++;
            _records.Add(copy);
            return copy.Id;
        }

        public int UpdateWithstand(WithstandTestRecord record)
        {
            BatchRecord r = Find(record.BatchDate, record.BatchNo);
            if (r == null) return 0;
            r.Voltage = record.Voltage;
            r.Resistance = record.Resistance;
            r.Current = record.Current;
            r.WithstandResult = (short)record.Result;
            r.WithstandTime = record.UploadTime;
            return 1;
        }

        public int UpdatePressure(PressureTestRecord record)
        {
            BatchRecord r = Find(record.BatchDate, record.BatchNo);
            if (r == null) return 0;
            r.Pressure = record.Pressure;
            r.PressureResult = (short)record.Result;
            r.PressureTime = record.UploadTime;
            return 1;
        }

        public IList<BatchRecord> Query(string fromDate, string toDate, string productName)
        {
            return _records
                .Where(r => string.CompareOrdinal(r.BatchDate, fromDate) >= 0 &&
                            string.CompareOrdinal(r.BatchDate, toDate) <= 0 &&
                            (string.IsNullOrEmpty(productName) ||
                             (r.ProductName ?? string.Empty).IndexOf(productName, StringComparison.OrdinalIgnoreCase) >= 0))
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
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
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
