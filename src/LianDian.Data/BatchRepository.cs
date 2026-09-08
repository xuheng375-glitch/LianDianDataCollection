using System;
using System.Collections.Generic;
using System.Data.SQLite;
using LianDian.Core.Models;

namespace LianDian.Data
{
    public interface IBatchRepository
    {
        int GetMaxBatchNo(string batchDate);
        long CountUnboundWithstand();
        bool Exists(string batchDate, int batchNo);
        bool ExistsByProduct(string batchDate, int batchNo, string productName);
        bool HasWithstandData(string batchDate, int batchNo);
        bool HasPressureData(string batchDate, int batchNo);
        long Insert(BatchRecord record);
        int UpdateWithstand(WithstandTestRecord record);
        int UpdatePressure(PressureTestRecord record);
        int BindQrGrade(string batchDate, int batchNo, string productName, string grade);
        IList<BatchRecord> Query(string fromDate, string toDate, string productName);
        IList<BatchRecord> QueryPage(string fromDate, string toDate, string productName, int offset, int limit);
        IEnumerable<BatchRecord> ExportRows(string fromDate, string toDate, string productName);
        IList<string> GetDistinctProductNames();
    }

    /// <summary>单表模型仓储：批次下发生成空记录，耐压/气压上传 UPDATE 同批次行。</summary>
    public sealed class BatchRepository : IBatchRepository, IPendingPlcAckStore
    {
        public long CountUnboundWithstand() => Convert.ToInt64(_context.ExecuteScalar(
            "SELECT COUNT(*) FROM batch_record WHERE withstand_result IS NULL"));

        private readonly DataContext _context;

        public BatchRepository(DataContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public int GetMaxBatchNo(string batchDate)
        {
            object result = _context.ExecuteScalar(
                "SELECT COALESCE(MAX(batch_no),0) FROM batch_record WHERE batch_date=@d",
                DataContext.Param("@d", batchDate));
            return Convert.ToInt32(result);
        }

        public bool Exists(string batchDate, int batchNo)
        {
            object result = _context.ExecuteScalar(
                "SELECT COUNT(1) FROM batch_record WHERE batch_date=@d AND batch_no=@n",
                DataContext.Param("@d", batchDate),
                DataContext.Param("@n", batchNo));
            return Convert.ToInt64(result) > 0;
        }

        public bool ExistsByProduct(string batchDate, int batchNo, string productName)
        {
            object result = _context.ExecuteScalar(
                "SELECT COUNT(1) FROM batch_record WHERE batch_date=@d AND batch_no=@n AND product_name=@p",
                DataContext.Param("@d", batchDate),
                DataContext.Param("@n", batchNo),
                DataContext.Param("@p", productName));
            return Convert.ToInt64(result) > 0;
        }

        public bool HasWithstandData(string batchDate, int batchNo)
        {
            object result = _context.ExecuteScalar(
                "SELECT COUNT(1) FROM batch_record WHERE batch_date=@d AND batch_no=@n AND withstand_result IS NOT NULL",
                DataContext.Param("@d", batchDate),
                DataContext.Param("@n", batchNo));
            return Convert.ToInt64(result) > 0;
        }

        public bool HasPressureData(string batchDate, int batchNo)
        {
            object result = _context.ExecuteScalar(
                "SELECT COUNT(1) FROM batch_record WHERE batch_date=@d AND batch_no=@n AND pressure_result IS NOT NULL",
                DataContext.Param("@d", batchDate),
                DataContext.Param("@n", batchNo));
            return Convert.ToInt64(result) > 0;
        }

        public long Insert(BatchRecord record)
        {
            const string sql = @"
INSERT INTO batch_record (batch_date, batch_no, employee_no, product_name, issue_time, qr_grade)
VALUES (@d, @n, @e, @p, @t, @g)";
            try
            {
                using (SQLiteConnection conn = _context.OpenConnection())
                using (SQLiteCommand cmd = _context.CreateCommand(conn, sql,
                    DataContext.Param("@d", record.BatchDate), DataContext.Param("@n", record.BatchNo),
                    DataContext.Param("@e", record.EmployeeNo), DataContext.Param("@p", record.ProductName),
                    DataContext.Param("@t", record.IssueTime), DataContext.Param("@g", record.QrGrade)))
                {
                    using (var transaction = conn.BeginTransaction())
                    {
                    cmd.Transaction = transaction;
                    cmd.ExecuteNonQuery();
                    cmd.Parameters.Clear();
                    cmd.CommandText = "SELECT last_insert_rowid()";
                    long id = Convert.ToInt64(cmd.ExecuteScalar());
                    if (!string.IsNullOrWhiteSpace(record.ProductName))
                    {
                        cmd.CommandText = @"INSERT INTO product_catalog(product_name,first_seen_at,last_seen_at) VALUES(@p,@t,@t)
                            ON CONFLICT(product_name) DO UPDATE SET last_seen_at=excluded.last_seen_at";
                        cmd.Parameters.Clear();
                        cmd.Parameters.Add(DataContext.Param("@p", record.ProductName));
                        cmd.Parameters.Add(DataContext.Param("@t", record.IssueTime));
                        cmd.ExecuteNonQuery();
                    }
                    SavePendingAck(conn, transaction, 4002, record.BatchDate, record.BatchNo);
                    transaction.Commit();
                    return id;
                    }
                }
            }
            catch (SQLiteException ex) when (ex.ResultCode == SQLiteErrorCode.Constraint && Exists(record.BatchDate, record.BatchNo))
            {
                throw new DuplicateKeyException(
                    string.Format("批次 {0}-{1} 已存在（uk_batch 冲突）", record.BatchDate, record.BatchNo), ex);
            }
        }

        public int BindQrGrade(string batchDate, int batchNo, string productName, string grade)
        {
            if (string.IsNullOrWhiteSpace(productName))
                throw new ArgumentException("二维码上传产品名称不能为空。", nameof(productName));
            if (grade == null || grade.Length != 1 || grade[0] < 'A' || grade[0] > 'F')
                throw new ArgumentException("二维码等级必须为A～F。", nameof(grade));
            // 相同等级允许幂等重传；已有不同等级不自动覆盖。
            return UpdateAndQueueAck(4004, batchDate, batchNo,
                "UPDATE batch_record SET qr_grade=@g WHERE batch_date=@d AND batch_no=@n AND product_name=@p AND (qr_grade IS NULL OR qr_grade=@g)",
                DataContext.Param("@g", grade), DataContext.Param("@d", batchDate),
                DataContext.Param("@n", batchNo), DataContext.Param("@p", productName));
        }

        public int UpdateWithstand(WithstandTestRecord record)
        {
            return UpdateAndQueueAck(4010, record.BatchDate, record.BatchNo,
                @"UPDATE batch_record
                  SET voltage=@v, resistance=@r, current=@c, withstand_result=@res, withstand_time=@time
                  WHERE batch_date=@d AND batch_no=@n AND withstand_result IS NULL",
                DataContext.Param("@v", record.Voltage),
                DataContext.Param("@r", record.Resistance),
                DataContext.Param("@c", record.Current),
                DataContext.Param("@res", record.Result),
                DataContext.Param("@time", record.UploadTime == DateTime.MinValue ? DateTime.Now : record.UploadTime),
                DataContext.Param("@d", record.BatchDate),
                DataContext.Param("@n", record.BatchNo));
        }

        public int UpdatePressure(PressureTestRecord record)
        {
            return UpdateAndQueueAck(4020, record.BatchDate, record.BatchNo,
                @"UPDATE batch_record
                  SET pressure=@p, pressure_result=@res, pressure_time=@time
                  WHERE batch_date=@d AND batch_no=@n AND pressure_result IS NULL",
                DataContext.Param("@p", record.Pressure),
                DataContext.Param("@res", record.Result),
                DataContext.Param("@time", record.UploadTime == DateTime.MinValue ? DateTime.Now : record.UploadTime),
                DataContext.Param("@d", record.BatchDate),
                DataContext.Param("@n", record.BatchNo));
        }

        public IList<BatchRecord> Query(string fromDate, string toDate, string productName)
            => QueryPage(fromDate, toDate, productName, 0, int.MaxValue);

        private int UpdateAndQueueAck(int channel, string date, int batchNo, string sql, params SQLiteParameter[] parameters)
        {
            using (var conn = _context.OpenConnection())
            using (var transaction = conn.BeginTransaction())
            using (var cmd = _context.CreateCommand(conn, sql, parameters))
            {
                cmd.Transaction = transaction;
                int affected = cmd.ExecuteNonQuery();
                if (affected > 0) SavePendingAck(conn, transaction, channel, date, batchNo);
                transaction.Commit();
                return affected;
            }
        }

        private void SavePendingAck(SQLiteConnection conn, SQLiteTransaction transaction, int channel, string date, int batchNo)
        {
            using (var check = _context.CreateCommand(conn,
                "SELECT COUNT(*) FROM plc_pending_ack WHERE channel=@c AND (batch_date<>@d OR batch_no<>@n)",
                DataContext.Param("@c", channel), DataContext.Param("@d", date), DataContext.Param("@n", batchNo)))
            {
                check.Transaction = transaction;
                if (Convert.ToInt64(check.ExecuteScalar()) != 0)
                    throw new InvalidOperationException("该 PLC 通道仍有上一批未确认应答，暂停新数据提交，请检查握手状态。");
            }
            using (var cmd = _context.CreateCommand(conn,
                "INSERT OR REPLACE INTO plc_pending_ack(channel,batch_date,batch_no) VALUES(@c,@d,@n)",
                DataContext.Param("@c",channel), DataContext.Param("@d",date), DataContext.Param("@n",batchNo)))
            { cmd.Transaction = transaction; cmd.ExecuteNonQuery(); }
        }

        public PendingPlcAck GetPendingAck(int channel)
        {
            using (var conn = _context.OpenConnection())
            using (var cmd = _context.CreateCommand(conn,"SELECT batch_date,batch_no FROM plc_pending_ack WHERE channel=@c", DataContext.Param("@c",channel)))
            using (var reader = cmd.ExecuteReader())
                return reader.Read() ? new PendingPlcAck { Channel=channel, BatchDate=reader.GetString(0), BatchNo=reader.GetInt32(1) } : null;
        }

        public void ClearPendingAck(int channel, string date, int batchNo) => _context.ExecuteNonQuery(
            "DELETE FROM plc_pending_ack WHERE channel=@c AND batch_date=@d AND batch_no=@n",
            DataContext.Param("@c",channel),DataContext.Param("@d",date),DataContext.Param("@n",batchNo));

        public IList<BatchRecord> QueryPage(string fromDate, string toDate, string productName, int offset, int limit)
            => ReadPage(fromDate, toDate, productName, offset, limit, long.MaxValue, null, 0);

        public IEnumerable<BatchRecord> ExportRows(string fromDate, string toDate, string productName)
        {
            long cutoff = Convert.ToInt64(_context.ExecuteScalar("SELECT COALESCE(MAX(id),0) FROM batch_record"));
            string lastDate = null;
            int lastNo = 0;
            while (true)
            {
                var page = ReadPage(fromDate, toDate, productName, 0, 1000, cutoff, lastDate, lastNo);
                if (page.Count == 0) yield break;
                foreach (var row in page) yield return row;
                lastDate = page[page.Count - 1].BatchDate;
                lastNo = page[page.Count - 1].BatchNo;
            }
        }

        private IList<BatchRecord> ReadPage(string fromDate, string toDate, string productName, int offset, int limit,
            long cutoff, string beforeDate, int beforeNo)
        {
            if (offset < 0 || limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));
            if (string.CompareOrdinal(fromDate, toDate) > 0) throw new ArgumentException("开始日期不能晚于结束日期。");
            var parameters = new List<SQLiteParameter>
            {
                DataContext.Param("@from", fromDate),
                // 将标量索引上界一起推进；仅附加元组条件会导致部分执行计划反复扫描已导出的日期。
                DataContext.Param("@to", beforeDate != null && string.CompareOrdinal(beforeDate, toDate) < 0 ? beforeDate : toDate)
            };
            string sql = @"SELECT id, batch_date, batch_no, employee_no, product_name, issue_time,
                           voltage, resistance, current, withstand_result, withstand_time,
                           pressure, pressure_result, pressure_time, qr_grade
                           FROM batch_record
                           WHERE batch_date >= @from AND batch_date <= @to";
            if (!string.IsNullOrEmpty(productName))
            {
                sql += " AND product_name LIKE @p";
                parameters.Add(DataContext.Param("@p", "%" + productName + "%"));
            }
            sql += " AND id<=@cutoff";
            parameters.Add(DataContext.Param("@cutoff", cutoff));
            if (beforeDate != null)
            {
                sql += " AND (batch_date,batch_no)<(@beforeDate,@beforeNo)";
                parameters.Add(DataContext.Param("@beforeDate", beforeDate));
                parameters.Add(DataContext.Param("@beforeNo", beforeNo));
            }
            sql += " ORDER BY batch_date DESC, batch_no DESC LIMIT @limit OFFSET @offset";
            parameters.Add(DataContext.Param("@limit", limit));
            parameters.Add(DataContext.Param("@offset", offset));

            var result = new List<BatchRecord>();
            using (SQLiteConnection conn = _context.OpenConnection())
            using (SQLiteCommand cmd = _context.CreateCommand(conn, sql, parameters.ToArray()))
            using (SQLiteDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var record = new BatchRecord
                    {
                        Id = reader.GetInt64(0),
                        QrGrade = reader.IsDBNull(14) ? null : reader.GetString(14),
                        BatchDate = reader.GetString(1),
                        BatchNo = reader.GetInt32(2),
                        EmployeeNo = reader.IsDBNull(3) ? null : reader.GetString(3),
                        ProductName = reader.IsDBNull(4) ? null : reader.GetString(4),
                        IssueTime = reader.IsDBNull(5) ? DateTime.MinValue : reader.GetDateTime(5),
                        Voltage = reader.IsDBNull(6) ? null : (decimal?)reader.GetDecimal(6),
                        Resistance = reader.IsDBNull(7) ? null : (decimal?)reader.GetDecimal(7),
                        Current = reader.IsDBNull(8) ? null : (decimal?)reader.GetDecimal(8),
                        WithstandResult = reader.IsDBNull(9) ? (short?)null : reader.GetInt16(9),
                        WithstandTime = reader.IsDBNull(10) ? (DateTime?)null : reader.GetDateTime(10),
                        Pressure = reader.IsDBNull(11) ? null : (decimal?)reader.GetDecimal(11),
                        PressureResult = reader.IsDBNull(12) ? (short?)null : reader.GetInt16(12),
                        PressureTime = reader.IsDBNull(13) ? (DateTime?)null : reader.GetDateTime(13)
                    };
                    result.Add(record);
                }
            }
            return result;
        }

        public IList<string> GetDistinctProductNames()
        {
            var result = new List<string>();
            using (SQLiteConnection conn = _context.OpenConnection())
            using (SQLiteCommand cmd = _context.CreateCommand(conn,
                "SELECT product_name FROM product_catalog ORDER BY product_name"))
            using (SQLiteDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                    result.Add(reader.GetString(0));
            }
            return result;
        }
    }
}
