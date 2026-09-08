using System;
using LianDian.Core;
using LianDian.Data;
using log4net;

namespace LianDian.Business.Services
{
    /// <summary>PLC 已离开等待态时收敛数据库 ACK；清理失败保留记录并在下一轮继续尝试。</summary>
    internal static class PendingAckCoordinator
    {
        public static bool TryReconcileIdle(IBatchRepository repository, int channel, int observedFlag,
            ILog log, Action<string> reportError)
        {
            if (observedFlag == (int)FlagState.Waiting) return true;
            try
            {
                var store = repository as IPendingPlcAckStore;
                var pending = store?.GetPendingAck(channel);
                if (pending == null) return true;
                store.ClearPendingAck(channel, pending.BatchDate, pending.BatchNo);
                log.InfoFormat("PLC D{0} 已离开等待态({1})，清理待确认 ACK：{2}-{3}",
                    channel, observedFlag, pending.BatchDate, pending.BatchNo);
                return true;
            }
            catch (Exception ex)
            {
                string message = string.Format("PLC D{0} ACK 协调失败，将在下一周期重试：{1}", channel, ex.Message);
                log.Error(message, ex);
                reportError?.Invoke(message);
                return false;
            }
        }
    }
}
