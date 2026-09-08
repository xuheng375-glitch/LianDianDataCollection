using System.Collections.Generic;
using LianDian.Comm;

namespace LianDian.Business.Tests.Fakes
{
    /// <summary>内存快照读取器：单寄存器值 + 字符串值，缺省 0/空。</summary>
    public sealed class FakeSnapshot : IPlcSnapshotReader
    {
        private readonly Dictionary<int, ushort> _words = new Dictionary<int, ushort>();
        private readonly Dictionary<int, string> _strings = new Dictionary<int, string>();
        private readonly bool _lowWordFirst;

        public FakeSnapshot(bool wordOrder = true)
        {
            _lowWordFirst = wordOrder;
        }

        public void SetWord(int dAddress, ushort value)
        {
            _words[dAddress] = value;
        }

        public void SetDInt(int dAddress, int value)
        {
            ushort[] regs = ModbusCodec.FromDInt(value, _lowWordFirst);
            _words[dAddress] = regs[0];
            _words[dAddress + 1] = regs[1];
        }

        public void SetString(int dAddress, string value)
        {
            _strings[dAddress] = value ?? string.Empty;
        }

        public bool TryGetDInt(int dAddress, out int value)
        {
            ushort low;
            ushort high;
            if (!_words.TryGetValue(dAddress, out low)) low = 0;
            if (!_words.TryGetValue(dAddress + 1, out high)) high = 0;
            value = _lowWordFirst
                ? ModbusCodec.ToDInt(new[] { low, high }, 0, true)
                : ModbusCodec.ToDInt(new[] { low, high }, 0, false);
            return true;
        }

        public bool TryGetString(int dAddress, out string value)
        {
            return _strings.TryGetValue(dAddress, out value);
        }

        public bool TryGetWord(int dAddress, out ushort value)
        {
            if (!_words.TryGetValue(dAddress, out value)) value = 0;
            return true;
        }
    }
}
