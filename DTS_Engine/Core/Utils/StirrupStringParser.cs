using System;
using System.Text.RegularExpressions;

namespace DTS_Engine.Core.Utils
{
    /// <summary>
    /// Parser cho chuỗi thép đai (VD: "d8@150", "2d10@100").
    /// </summary>
    public static class StirrupStringParser
    {
        // Pattern: [n]d[dia]@[spacing] or d[dia]@[spacing]([n]l)
        private static readonly Regex StirrupPattern = new Regex(
            @"(\d*)\s*[dDfF]?(?:phi|fi|Ø)?\s*(\d+)\s*@\s*(\d+)(?:\s*\(\s*(\d+)\s*[lL]\s*\))?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Parse chuỗi thép đai thành diện tích thép trên 1 mét dài (cm2/m).
        /// </summary>
        public static double ParseAsProv(string stirrupStr)
        {
            if (string.IsNullOrWhiteSpace(stirrupStr)) return 0;

            var match = StirrupPattern.Match(stirrupStr.Trim());
            if (match.Success)
            {
                int count = GetLegs(stirrupStr);
                int diameter = int.Parse(match.Groups[2].Value);
                int spacing = int.Parse(match.Groups[3].Value);

                if (spacing <= 0) return 0;

                // Diện tích 1 thanh (cm2)
                double areaPerBar = Math.PI * diameter * diameter / 400.0;

                // Tổng diện tích trên 1 mét (cm2/m)
                // n * As1 * (1000 / s)
                return count * areaPerBar * (1000.0 / spacing);
            }

            return 0;
        }

        public static int GetLegs(string stirrupStr)
        {
            if (string.IsNullOrWhiteSpace(stirrupStr)) return 0;

            var match = StirrupPattern.Match(stirrupStr.Trim());
            if (match.Success)
            {
                // Ưu tiên suffix: (4l) -> 4
                string suffixLegs = match.Groups[4].Value;
                if (!string.IsNullOrEmpty(suffixLegs)) return int.Parse(suffixLegs);

                // Ưu tiên prefix: 2d8 -> 2
                string prefixLegs = match.Groups[1].Value;
                if (!string.IsNullOrEmpty(prefixLegs)) return int.Parse(prefixLegs);
            }
            return 0; // No default, force explicit
        }
    }
}
