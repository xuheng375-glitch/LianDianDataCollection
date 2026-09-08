using System;

namespace LianDian.Comm
{
    /// <summary>PLC 读写客户端。所有方法均使用逻辑 D 号（如 4002），物理偏移在实现内换算。</summary>
    public interface IPlcClient : IDisposable
    {
        bool IsConnected { get; }

        event EventHandler ConnectionChanged;

        void Connect();

        void Disconnect();

        /// <summary>读 DINT（占 2 个寄存器）。</summary>
        int ReadDInt(int dAddress);

        /// <summary>写 DINT（占 2 个寄存器）。</summary>
        void WriteDInt(int dAddress, int value);
        void WriteString(int dAddress, string value);

        /// <summary>读字符串（按指定寄存器数）。</summary>
        string ReadString(int dAddress, int registerCount);

        /// <summary>读连续寄存器。</summary>
        ushort[] ReadRegisters(int dAddress, int count);
    }

    /// <summary>快照读取接口：业务层从轮询快照取数，不直接发请求。</summary>
    public interface IPlcSnapshotReader
    {
        bool TryGetDInt(int dAddress, out int value);

        bool TryGetString(int dAddress, out string value);

        /// <summary>读取单个 WORD；DINT 字段应使用 TryGetDInt。</summary>
        bool TryGetWord(int dAddress, out ushort value);
    }
}
