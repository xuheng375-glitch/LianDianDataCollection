using System;
using System.Collections.Generic;
using LianDian.Core.Config;

namespace LianDian.Comm
{
    public interface IPlcSnapshotSource { IPlcSnapshotReader Capture(); }
    /// <summary>一个完整轮询周期的不可变数据，过期后拒绝提供生产数据。</summary>
    public sealed class PlcSnapshot : IPlcSnapshotReader
    {
        private readonly Dictionary<int, ushort[]> _data;
        private readonly PlcConfig _config;
        private readonly int _created = Environment.TickCount;
        public PlcSnapshot(Dictionary<int, ushort[]> data, PlcConfig config)
        {
            _data = new Dictionary<int, ushort[]>();
            foreach (var item in data) _data[item.Key] = (ushort[])item.Value.Clone();
            _config = config;
        }
        private bool Get(int address, int count, out ushort[] data)
        {
            data = null;
            return unchecked((uint)(Environment.TickCount - _created)) <= _config.SnapshotMaxAgeMs &&
                _data.TryGetValue(address, out data) && data.Length >= count;
        }
        public bool TryGetDInt(int address, out int value)
        {
            value = 0;
            if (!Get(address, 2, out var data)) return false;
            value = ModbusCodec.ToDInt(data, 0, _config.WordOrder); return true;
        }
        public bool TryGetWord(int address, out ushort value)
        {
            value = 0;
            if (!Get(address, 1, out var data)) return false;
            value = data[0]; return true;
        }
        public bool TryGetString(int address, out string value)
        {
            value = null;
            if (!RegisterMap.StringRegisterCounts.ContainsKey(address)) return false;
            int count = RegisterMap.StringCount(address, _config);
            if (!Get(address, count, out var data)) return false;
            value = ModbusCodec.DecodeString(data, 0, count, ModbusTcpPlcClient.ResolveEncoding(_config.StringEncoding), _config.StringLowByteFirst);
            return true;
        }
    }
}
