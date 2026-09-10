using System;
using System.Globalization;

namespace LianDian.Core
{
    public static class Extensions
    {
        /// <summary>批次日期编码：年2位 + 年内天数3位，如 2026 年第 188 天 → "26188"。</summary>
        public static string ToBatchDateString(this DateTime dt)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:00}{1:000}", dt.Year % 100, dt.DayOfYear);
        }

        public static decimal? SafeToDecimal(this string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            decimal result;
            if (decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint |
                NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture, out result))
            {
                // Decimal 会将小于其精度的非零数舍入为零，不能把它当作有效测量值。
                if (result == 0)
                    foreach (char c in value)
                    {
                        if (c == 'e' || c == 'E') break;
                        if (c >= '1' && c <= '9') return null;
                    }
                return result;
            }
            return null;
        }

        public static int? SafeToInt(this string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            int result;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
                return result;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out result))
                return result;
            return null;
        }

        public static long? SafeToLong(this string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            long result;
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
                return result;
            return null;
        }

        /// <summary>net472 兼容的忽略大小写包含判断。</summary>
        public static bool ContainsIgnoreCase(this string source, string value)
        {
            if (source == null || value == null) return false;
            return source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
