using System.Collections.Generic;

namespace LianDian.Comm
{
    /// <summary>
    /// PLC 寄存器地址唯一事实来源，按用户最新点位表更新，参见 docs/PLC点位表.md。
    /// 逻辑 D 号 → Modbus 物理寄存器 = D - 1 + ModbusOffset。
    /// </summary>
    public static class RegisterMap
    {
        // 心跳位：纯写入（0-1 反转），绝不允许出现在任何读操作中。
        public const int D4000_Heartbeat = 4000;

        // 批次下发
        public const int D4002_BatchIssueFlag = 4002;
        public const int D5700_BatchIssueDate = 5700;
        public const int D4100_BatchIssueNo = 4100;

        // 耐压上传
        public const int D4010_WithstandFlag = 4010;
        public const int D5800_WithstandDate = 5800;
        public const int D4200_WithstandBatchNo = 4200;
        public const int D4014_WithstandResult = 4014;

        // 气压上传
        public const int D4020_PressureFlag = 4020;
        public const int D5900_PressureDate = 5900;
        public const int D4300_PressureBatchNo = 4300;
        public const int D4024_PressureResult = 4024;

        // 员工工号：完整 DINT，占 D4030、D4031，支持六位工号。
        public const int D4030_EmployeeNo = 4030;
        public const int D6000_QrGrade = 6000;

        // 字符串区（STRING）
        public const int D5000_ProductName = 5000;
        public const int D5100_WithstandProductName = 5100;
        public const int D5200_PressureProductName = 5200;
        public const int D5300_Voltage = 5300;
        public const int D5400_Resistance = 5400;
        public const int D5500_Current = 5500;
        public const int D5600_PressureValue = 5600;

        /// <summary>各 STRING 点占用寄存器数（现场校准项，默认按 20/12 字节容量）。</summary>
        public static readonly Dictionary<int, int> StringRegisterCounts = new Dictionary<int, int>
        {
            { D5000_ProductName, 10 },        // 20 字节
            { D5100_WithstandProductName, 10 },
            { D5200_PressureProductName, 10 },
            { D5300_Voltage, 6 },             // 12 字节
            { D5400_Resistance, 6 },
            { D5500_Current, 6 },
            { D5600_PressureValue, 6 },
            { D4100_BatchIssueNo, 3 }, { D4200_WithstandBatchNo, 3 }, { D4300_PressureBatchNo, 3 },
            { D5700_BatchIssueDate, 3 }, { D5800_WithstandDate, 3 }, { D5900_PressureDate, 3 },
            { D6000_QrGrade, 1 }
        };

        /// <summary>
        /// 快照按有效点位分别读取，员工工号读取完整 DINT；心跳、下发字符串不轮询。
        /// 二维码等级在批次下发标志为 1 时直接读取，不使用轮询缓存。
        /// + 各字符串块。失败字段本周期失效，不使用旧值处理生产请求。
        /// </summary>
        public static readonly List<PollBlock> PollBlocks = new List<PollBlock>
        {
            new PollBlock(D4002_BatchIssueFlag, 2, BlockType.DInt),
            new PollBlock(D4010_WithstandFlag, 2, BlockType.DInt),
            new PollBlock(D4014_WithstandResult, 2, BlockType.DInt),
            new PollBlock(D4020_PressureFlag, 2, BlockType.DInt),
            new PollBlock(D4024_PressureResult, 2, BlockType.DInt),
            new PollBlock(D4030_EmployeeNo, 2, BlockType.DInt),
            new PollBlock(D4200_WithstandBatchNo, 3, BlockType.String),
            new PollBlock(D4300_PressureBatchNo, 3, BlockType.String),
            new PollBlock(D5800_WithstandDate, 3, BlockType.String),
            new PollBlock(D5900_PressureDate, 3, BlockType.String),
            new PollBlock(D5000_ProductName, StringRegisterCounts[D5000_ProductName], BlockType.String),
            new PollBlock(D5100_WithstandProductName, StringRegisterCounts[D5100_WithstandProductName], BlockType.String),
            new PollBlock(D5200_PressureProductName, StringRegisterCounts[D5200_PressureProductName], BlockType.String),
            new PollBlock(D5300_Voltage, StringRegisterCounts[D5300_Voltage], BlockType.String),
            new PollBlock(D5400_Resistance, StringRegisterCounts[D5400_Resistance], BlockType.String),
            new PollBlock(D5500_Current, StringRegisterCounts[D5500_Current], BlockType.String),
            new PollBlock(D5600_PressureValue, StringRegisterCounts[D5600_PressureValue], BlockType.String)
        };

        /// <summary>逻辑 D 号 → Modbus 物理寄存器索引（Mitsubishi 约定 D1=寄存器0）。</summary>
        public static int StringCount(int address, LianDian.Core.Config.PlcConfig config)
        {
            if (address == D4100_BatchIssueNo || address == D4200_WithstandBatchNo || address == D4300_PressureBatchNo)
                return config.BatchStringRegisters;
            if (address == D5700_BatchIssueDate || address == D5800_WithstandDate || address == D5900_PressureDate)
                return config.DateStringRegisters;
            if (address == D6000_QrGrade) return config.QrGradeStringRegisters;
            return StringRegisterCounts[address];
        }

        public static ushort ToModbus(int dAddress, int modbusOffset)
        {
            return (ushort)(dAddress - 1 + modbusOffset);
        }
    }

    public enum BlockType
    {
        DInt = 0,
        String = 1
    }

    /// <summary>轮询块定义：起始逻辑 D 号 + 寄存器数 + 类型。</summary>
    public sealed class PollBlock
    {
        public PollBlock(int startAddress, int count, BlockType type)
        {
            StartAddress = startAddress;
            Count = count;
            Type = type;
        }

        public int StartAddress { get; }
        public int Count { get; }
        public BlockType Type { get; }
    }
}
