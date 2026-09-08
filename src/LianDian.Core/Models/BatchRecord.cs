using System;

namespace LianDian.Core.Models
{
    /// <summary>
    /// 单表核心实体 batch_record：批次下发生成空测试字段记录，耐压/气压上传 UPDATE 同批次行。
    /// </summary>
    public class BatchRecord
    {
        public long Id { get; set; }
        public string BatchDate { get; set; }
        public int BatchNo { get; set; }
        public string EmployeeNo { get; set; }
        public string ProductName { get; set; }
        public string QrGrade { get; set; }
        public DateTime IssueTime { get; set; }

        public decimal? Voltage { get; set; }
        public decimal? Resistance { get; set; }
        public decimal? Current { get; set; }
        public short? WithstandResult { get; set; }
        public DateTime? WithstandTime { get; set; }

        public decimal? Pressure { get; set; }
        public short? PressureResult { get; set; }
        public DateTime? PressureTime { get; set; }
    }
}
