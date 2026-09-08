using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace LianDian.Core.Config
{
    /// <summary>
    /// 轻量 INI 读写器：UTF-8、分节、支持 ; # 整行注释、大小写不敏感、保存与重载。
    /// </summary>
    public sealed class IniFile
    {
        private readonly string _path;
        private readonly Dictionary<string, Dictionary<string, string>> _data;
        private readonly List<string> _sectionOrder;

        public IniFile(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _data = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            _sectionOrder = new List<string>();
            Reload();
        }

        public string Path => _path;

        public void Reload()
        {
            _data.Clear();
            _sectionOrder.Clear();
            if (!File.Exists(_path)) return;

            string currentSection = string.Empty;
            foreach (string rawLine in File.ReadAllLines(_path, Encoding.UTF8))
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;
                if (line[0] == ';' || line[0] == '#') continue;

                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    currentSection = line.Substring(1, line.Length - 2).Trim();
                    if (!_data.ContainsKey(currentSection))
                    {
                        _data[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        _sectionOrder.Add(currentSection);
                    }
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                if (key.Length == 0) continue;

                Dictionary<string, string> section;
                if (!_data.TryGetValue(currentSection, out section))
                {
                    section = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    _data[currentSection] = section;
                    _sectionOrder.Add(currentSection);
                }
                section[key] = value;
            }
        }

        public string Get(string section, string key, string defaultValue = null)
        {
            Dictionary<string, string> table;
            string value;
            if (_data.TryGetValue(section, out table) && table.TryGetValue(key, out value))
                return value;
            return defaultValue;
        }

        public int GetInt(string section, string key, int defaultValue = 0)
        {
            string v = Get(section, key, null);
            int result;
            return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : defaultValue;
        }

        public long GetLong(string section, string key, long defaultValue = 0)
        {
            string v = Get(section, key, null);
            long result;
            return long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : defaultValue;
        }

        public double GetDouble(string section, string key, double defaultValue = 0)
        {
            string v = Get(section, key, null);
            double result;
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ? result : defaultValue;
        }

        public bool GetBool(string section, string key, bool defaultValue = false)
        {
            string v = Get(section, key, null);
            if (v == null) return defaultValue;
            bool result;
            if (bool.TryParse(v, out result)) return result;
            if (v == "1" || v.Equals("yes", StringComparison.OrdinalIgnoreCase)) return true;
            if (v == "0" || v.Equals("no", StringComparison.OrdinalIgnoreCase)) return false;
            return defaultValue;
        }

        public TimeSpan GetTimeSpan(string section, string key, TimeSpan defaultValue)
        {
            string v = Get(section, key, null);
            TimeSpan result;
            return TimeSpan.TryParse(v, CultureInfo.InvariantCulture, out result) ? result : defaultValue;
        }

        public void Set(string section, string key, string value)
        {
            if (section == null) throw new ArgumentNullException(nameof(section));
            if (key == null) throw new ArgumentNullException(nameof(key));
            Dictionary<string, string> table;
            if (!_data.TryGetValue(section, out table))
            {
                table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _data[section] = table;
                _sectionOrder.Add(section);
            }
            table[key] = value ?? string.Empty;
        }

        public void Save()
        {
            var sb = new StringBuilder();
            foreach (string section in _sectionOrder)
            {
                sb.Append('[').Append(section).AppendLine("]");
                Dictionary<string, string> table = _data[section];
                foreach (KeyValuePair<string, string> kvp in table)
                    sb.Append(kvp.Key).Append('=').AppendLine(kvp.Value);
                sb.AppendLine();
            }
            string dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_path, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
