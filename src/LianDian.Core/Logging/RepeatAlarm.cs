using System;
using System.Collections.Generic;

namespace LianDian.Core.Logging
{
    /// <summary>只限制重复告警输出，不限制业务重试；恢复后同类故障立即重新报告。</summary>
    public sealed class RepeatAlarm
    {
        private readonly Dictionary<string, DateTime> _last = new Dictionary<string, DateTime>();
        private readonly Func<DateTime> _now;
        public RepeatAlarm() : this(() => DateTime.UtcNow) { }
        public RepeatAlarm(Func<DateTime> now) { _now = now; }
        public bool ShouldReport(string message)
        {
            lock (_last)
            {
                DateTime now = _now();
                if (_last.TryGetValue(message, out DateTime previous) && now - previous < TimeSpan.FromSeconds(30)) return false;
                if (_last.Count >= 256) _last.Clear();
                _last[message] = now;
                return true;
            }
        }
        public bool Reset()
        {
            lock (_last) { bool hadErrors = _last.Count > 0; _last.Clear(); return hadErrors; }
        }
    }
}
