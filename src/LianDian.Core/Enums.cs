namespace LianDian.Core
{
    /// <summary>PLC 业务标志位语义（红线：1=等待处理，2=处理成功，3=错误/历史数据）。</summary>
    public enum FlagState
    {
        Waiting = 1,
        Success = 2,
        Error = 3
    }

    /// <summary>测试结果：1=OK，2=NG。</summary>
    public enum TestResult
    {
        Ok = 1,
        Ng = 2
    }

    /// <summary>心跳发送状态。</summary>
    public enum HeartbeatState
    {
        Stopped = 0,
        Running = 1
    }

    /// <summary>连接状态。</summary>
    public enum ConnectState
    {
        Disconnected = 0,
        Connecting = 1,
        Connected = 2
    }
}
