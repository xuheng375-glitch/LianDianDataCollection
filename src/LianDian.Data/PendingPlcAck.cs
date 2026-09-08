namespace LianDian.Data
{
    public sealed class PendingPlcAck
    {
        public int Channel { get; set; }
        public string BatchDate { get; set; }
        public int BatchNo { get; set; }
    }
    public interface IPendingPlcAckStore
    {
        PendingPlcAck GetPendingAck(int channel);
        void ClearPendingAck(int channel, string date, int batchNo);
    }
}
