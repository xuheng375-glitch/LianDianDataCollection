using System;

namespace LianDian.Core.Models
{
    /// <summary>耐压上传事件载荷（不独立建表）。</summary>
    public class WithstandTestRecord
    {
        public string BatchDate { get; set; }
        public int BatchNo { get; set; }
        public string ProductName { get; set; }
        public string EmployeeNo { get; set; }
        public decimal? Voltage { get; set; }
        public decimal? Resistance { get; set; }
        public decimal? Current { get; set; }
        public int Result { get; set; }
        public DateTime UploadTime { get; set; }
    }

    /// <summary>气压上传事件载荷（不独立建表）。</summary>
    public class PressureTestRecord
    {
        public string BatchDate { get; set; }
        public int BatchNo { get; set; }
        public string ProductName { get; set; }
        public string EmployeeNo { get; set; }
        public decimal? Pressure { get; set; }
        public int Result { get; set; }
        public DateTime UploadTime { get; set; }
    }
}
