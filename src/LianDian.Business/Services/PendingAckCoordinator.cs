using System;
using System.Collections.Concurrent;
using LianDian.Core.Logging;
using LianDian.Core;
using LianDian.Data;
using log4net;

namespace LianDian.Business.Services
{
    /// <summary>PLC 已离开等待态时收敛数据库 ACK；清理失败保留记录并在下一轮继续尝试。</summary>
    internal static class PendingAckCoordinator
    {
        private static readonly ConcurrentDictionary<int, RepeatAlarm> Alarms = new ConcurrentDictionary<int, RepeatAlarm>();
        public static bool TryReconcileIdle(IBatchRepository repository, int channel, int observedFlag,
            ILog log, Action<string> reportError)
        {
            if (observedFlag == (int)FlagState.Waiting) return true;
            var alarm = Alarms.GetOrAdd(channel, _ => new RepeatAlarm());
            try
            {
                var store = repository as IPendingPlcAckStore;
                var pending = store?.GetPendingAck(channel);
                if (pending == null)
                {
                    if (alarm.Reset()) log.InfoFormat("PLC D{0} ACK 协调已恢复", channel);
                    return true;
                }
                store.ClearPendingAck(channel, pending.BatchDate, pending.BatchNo);
                alarm.Reset();
                log.InfoFormat("PLC D{0} 已离开等待态({1})，清理待确认 ACK：{2}-{3}",
                    channel, observedFlag, pending.BatchDate, pending.BatchNo);
                return true;
            }
            catch (Exception ex)
            {
                string message = string.Format("PLC D{0} ACK 协调失败，将在下一周期重试：{1}", channel, ex.Message);
                if (alarm.ShouldReport(message))
                {
                    log.Error(message, ex);
                    reportError?.Invoke(message);
                }
                return false;
            }
        }
    }
}
