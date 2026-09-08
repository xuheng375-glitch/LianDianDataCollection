using System;
using System.Globalization;
namespace LianDian.Comm
{
    public static class PlcText
    {
        private static bool FiveDigits(string text)
        {
            if (text == null || text.Length != 5) return false;
            foreach (char c in text) if (c < '0' || c > '9') return false;
            return true;
        }
        public static bool TryBatch(string text, out int number)
        {
            number = 0;
            return FiveDigits(text) && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0;
        }
        public static bool IsDate(string text)
        {
            if (!FiveDigits(text)) return false;
            int year = 2000 + int.Parse(text.Substring(0, 2), CultureInfo.InvariantCulture);
            int day = int.Parse(text.Substring(2), CultureInfo.InvariantCulture);
            return day >= 1 && day <= (DateTime.IsLeapYear(year) ? 366 : 365);
        }
    }
}
