using System;
using System.Globalization;

namespace BluesBar.Systems
{
    public static class NumberFormat
    {
        // 950 -> "950"
        // 1_200 -> "1.2K"
        // 2_540_000 -> "2.5M"
        // 4_000_000_000_000 -> "4T"
        public static string Abbrev(long value)
        {
            if (value < 0) value = 0;
            if (value < 1_000) return value.ToString(CultureInfo.InvariantCulture);

            string[] suffix = { "K", "M", "B", "T", "Qa", "Qi" }; // extend later if you go cosmic 😄
            double v = value;
            int idx = -1;

            while (v >= 1000 && idx < suffix.Length - 1)
            {
                v /= 1000.0;
                idx++;
            }

            // 1 decimal only when it adds meaning (1.2M), but avoid "12.0M"
            string fmt = (v < 10) ? "0.#" : "0";
            return v.ToString(fmt, CultureInfo.InvariantCulture) + suffix[idx];
        }

        public static string AbbrevMoney(long value)
        {
            return "$" + Abbrev(value);
        }
    }
}
