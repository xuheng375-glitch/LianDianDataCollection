using System;
using System.Data.SQLite;
using System.IO;
using LianDian.Core.Config;

namespace LianDian.Data
{
    /// <summary>SQLite 单文件数据库连接与执行封装。</summary>
    public sealed class DataContext
    {
        private readonly DbConfig _config;
        private readonly string _connectionString;
        public string FilePath { get; }

        public DataContext(DbConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            string path = config.FilePath;
            if (!Path.IsPathRooted(path)) path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
            path = Path.GetFullPath(path);
            FilePath = path;
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            _connectionString = new SQLiteConnectionStringBuilder
            {
                DataSource = path, ForeignKeys = true, JournalMode = SQLiteJournalModeEnum.Wal,
                SyncMode = SynchronizationModes.Full, Pooling = true, DefaultTimeout = 5
            }.ToString();
        }

        public string ConnectionString => _connectionString;

        public DbConfig Config => _config;

        public SQLiteConnection OpenConnection()
        {
            var conn = new SQLiteConnection(_connectionString);
            try { conn.Open(); return conn; }
            catch { conn.Dispose(); throw; }
        }

        /// <summary>检查主库存在、磁盘余量和真实提交能力，避免只读 SELECT 1 误报健康。</summary>
        public bool TestConnection()
        {
            try
            {
                if (!File.Exists(FilePath)) return false;
                var drive = new DriveInfo(Path.GetPathRoot(FilePath));
                if (drive.AvailableFreeSpace < (long)_config.MinimumFreeSpaceMb * 1024 * 1024) return false;
                using (SQLiteConnection conn = OpenConnection())
                using (SQLiteCommand cmd = CreateCommand(conn,
                    "UPDATE runtime_health SET checked_at=strftime('%Y-%m-%dT%H:%M:%f','now') WHERE id=1"))
                {
                    return cmd.ExecuteNonQuery() == 1;
                }
            }
            catch
            {
                return false;
            }
        }

        public int ExecuteNonQuery(string sql, params SQLiteParameter[] parameters)
        {
            using (SQLiteConnection conn = OpenConnection())
            using (SQLiteCommand cmd = CreateCommand(conn, sql, parameters))
                return cmd.ExecuteNonQuery();
        }

        public object ExecuteScalar(string sql, params SQLiteParameter[] parameters)
        {
            using (SQLiteConnection conn = OpenConnection())
            using (SQLiteCommand cmd = CreateCommand(conn, sql, parameters))
                return cmd.ExecuteScalar();
        }

        public SQLiteCommand CreateCommand(SQLiteConnection conn, string sql, params SQLiteParameter[] parameters)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 5;
            if (parameters != null)
            {
                foreach (SQLiteParameter p in parameters)
                    cmd.Parameters.Add(p);
            }
            return cmd;
        }

        public static SQLiteParameter Param(string name, object value)
        {
            return new SQLiteParameter(name, value ?? DBNull.Value);
        }
    }
}
