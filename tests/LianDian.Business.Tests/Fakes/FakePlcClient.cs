using System;
using System.Collections.Generic;
using System.Text;
using LianDian.Comm;

namespace LianDian.Business.Tests.Fakes
{
    /// <summary>内存 PLC 客户端：可预置寄存器、记录写入。</summary>
    public sealed class FakePlcClient : IPlcClient
    {
        private readonly Dictionary<int, ushort[]> _registers = new Dictionary<int, ushort[]>();
        private readonly bool _lowWordFirst;

        public FakePlcClient(bool wordOrder = true)
        {
            _lowWordFirst = wordOrder;
            SetString(RegisterMap.D6000_QrGrade, "A", 1);
        }

        public bool IsConnected { get; private set; }

        public event EventHandler ConnectionChanged;

        public IList<WriteRecord> Writes { get; } = new List<WriteRecord>();
        public IList<string> StringWrites { get; } = new List<string>();
        public Action<int> BeforeReadRegisters { get; set; }
        public Action<int> BeforeReadString { get; set; }
        public Action<int, int> BeforeWriteDInt { get; set; }

        public void SetDInt(int dAddress, int value)
        {
            _registers[dAddress] = ModbusCodec.FromDInt(value, _lowWordFirst);
        }

        public void SetString(int dAddress, string value, int registerCount = 10, bool lowByteFirst = true)
        {
            _registers[dAddress] = ModbusCodec.EncodeString(value, registerCount, Encoding.UTF8, lowByteFirst);
        }

        public void Connect()
        {
            IsConnected = true;
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Disconnect()
        {
            IsConnected = false;
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public int ReadDInt(int dAddress)
        {
            ushort[] regs = Get(dAddress, 2);
            return ModbusCodec.ToDInt(regs, 0, _lowWordFirst);
        }

        public void WriteDInt(int dAddress, int value)
        {
            BeforeWriteDInt?.Invoke(dAddress, value);
            Writes.Add(new WriteRecord(dAddress, value));
            _registers[dAddress] = ModbusCodec.FromDInt(value, _lowWordFirst);
        }

        public string ReadString(int dAddress, int registerCount)
        {
            BeforeReadString?.Invoke(dAddress);
            ushort[] regs = Get(dAddress, registerCount);
            return ModbusCodec.DecodeString(regs, 0, registerCount, Encoding.UTF8, true);
        }

        public ushort[] ReadRegisters(int dAddress, int count)
        {
            BeforeReadRegisters?.Invoke(dAddress);
            return (ushort[])Get(dAddress, count).Clone();
        }

        public void Dispose()
        {
            IsConnected = false;
        }

        public void WriteString(int dAddress, string value)
        {
            StringWrites.Add("D" + dAddress + "=" + value);
            SetString(dAddress, value, RegisterMap.StringRegisterCounts[dAddress]);
        }

        private ushort[] Get(int dAddress, int count)
        {
            ushort[] regs;
            if (!_registers.TryGetValue(dAddress, out regs) || regs.Length < count)
            {
                regs = new ushort[count];
                _registers[dAddress] = regs;
            }
            return regs;
        }
    }

    public sealed class WriteRecord
    {
        public WriteRecord(int address, int value)
        {
            Address = address;
            Value = value;
        }

        public int Address { get; }
        public int Value { get; }

        public override string ToString()
        {
            return "D" + Address + "=" + Value;
        }
    }
}
