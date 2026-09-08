using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using LianDian.Core.Config;
using LianDian.Core.Logging;
using log4net;

namespace LianDian.Data
{
    /// <summary>首次运行创建 SQLite 文件和表，并对旧版本表执行幂等补列。</summary>
    public sealed class DatabaseInitializer
    {
        private static readonly ILog Log = LogHelper.Get(LogHelper.Database);
        private readonly DataContext _context;

        public DatabaseInitializer(DbConfig config)
        {
            _context = new DataContext(config ?? throw new ArgumentNullException(nameof(config)));
        }

        public void EnsureCreated()
        {
            EnsureTable();
            EnsureBatchColumns();
            _context.ExecuteNonQuery("CREATE INDEX IF NOT EXISTS ix_batch_unbound_withstand ON batch_record(id) WHERE withstand_result IS NULL");
            _context.ExecuteNonQuery("CREATE INDEX IF NOT EXISTS ix_batch_product_date ON batch_record(product_name, batch_date, batch_no)");
            _context.ExecuteNonQuery("CREATE TABLE IF NOT EXISTS product_catalog(product_name TEXT NOT NULL PRIMARY KEY, first_seen_at TEXT NOT NULL, last_seen_at TEXT NOT NULL)");
            // 旧库只在目录表为空时执行一次事实表回填；正常启动不再扫描多年历史数据。
            if (Convert.ToInt64(_context.ExecuteScalar("SELECT COUNT(*) FROM product_catalog")) == 0)
                _context.ExecuteNonQuery(@"INSERT OR IGNORE INTO product_catalog(product_name,first_seen_at,last_seen_at)
                    SELECT product_name,COALESCE(MIN(issue_time),datetime('now')),COALESCE(MAX(issue_time),datetime('now'))
                    FROM batch_record WHERE product_name IS NOT NULL AND product_name<>'' GROUP BY product_name");
            _context.ExecuteNonQuery("CREATE TABLE IF NOT EXISTS runtime_health(id INTEGER PRIMARY KEY, checked_at TEXT); INSERT OR IGNORE INTO runtime_health(id) VALUES(1)");
            _context.ExecuteNonQuery("CREATE TABLE IF NOT EXISTS plc_pending_ack(channel INTEGER PRIMARY KEY, batch_date TEXT NOT NULL, batch_no INTEGER NOT NULL)");
            Log.Info("SQLite 数据库已就绪");
        }

        private void EnsureTable()
        {
            const string sql = @"
CREATE TABLE IF NOT EXISTS batch_record (
  id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
  batch_date TEXT NOT NULL,
  batch_no INTEGER NOT NULL,
  employee_no TEXT NULL,
  product_name TEXT NULL,
  issue_time TEXT NULL,
  voltage NUMERIC NULL,
  resistance NUMERIC NULL,
  current NUMERIC NULL,
  withstand_result INTEGER NULL,
  withstand_time TEXT NULL,
  pressure NUMERIC NULL,
  pressure_result INTEGER NULL,
  pressure_time TEXT NULL,
  qr_grade TEXT NULL,
  CONSTRAINT uk_batch UNIQUE (batch_date, batch_no)
);";
            _context.ExecuteNonQuery(sql);
        }

        public void EnsureBatchColumns()
        {
            IList<string> existing = GetExistingColumns();
            foreach (KeyValuePair<string, string> column in ColumnDefinitions)
            {
                if (existing.Any(c => string.Equals(c, column.Key, StringComparison.OrdinalIgnoreCase))) continue;
                _context.ExecuteNonQuery("ALTER TABLE batch_record ADD COLUMN \"" + column.Key + "\" " + column.Value);
                Log.InfoFormat("SQLite 旧表补列完成：{0}", column.Key);
            }
        }

        private IList<string> GetExistingColumns()
        {
            var result = new List<string>();
            using (SQLiteConnection conn = _context.OpenConnection())
            using (SQLiteCommand cmd = _context.CreateCommand(conn, "PRAGMA table_info(batch_record)"))
            using (SQLiteDataReader reader = cmd.ExecuteReader())
                while (reader.Read()) result.Add(reader.GetString(1));
            return result;
        }

        private static readonly Dictionary<string, string> ColumnDefinitions = new Dictionary<string, string>
        {
            { "employee_no", "TEXT NULL" }, { "product_name", "TEXT NULL" },
            { "qr_grade", "TEXT NULL" },
            { "issue_time", "TEXT NULL" }, { "voltage", "NUMERIC NULL" },
            { "resistance", "NUMERIC NULL" }, { "current", "NUMERIC NULL" },
            { "withstand_result", "INTEGER NULL" }, { "withstand_time", "TEXT NULL" },
            { "pressure", "NUMERIC NULL" }, { "pressure_result", "INTEGER NULL" },
            { "pressure_time", "TEXT NULL" }
        };
    }
}
