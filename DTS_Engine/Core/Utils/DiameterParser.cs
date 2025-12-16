using System;
using System.Collections.Generic;
using System.Linq;

namespace DTS_Engine.Core.Utils
{
    /// <summary>
    /// Parser thông minh cho input đường kính thép linh hoạt.
    /// Hỗ trợ: "16-25" (range), "18, 20, 22" (list), hoặc kết hợp "16-20, 25, 28"
    /// </summary>
    public static class DiameterParser
    {
        /// <summary>
        /// Parse input string thành danh sách đường kính, lọc theo kho thép (inventory).
        /// </summary>
        /// <param name="input">Input string: "16-25" hoặc "18, 20, 22" hoặc "16-20, 25"</param>
        /// <param name="inventory">Danh sách đường kính có sẵn trong kho</param>
        /// <returns>Danh sách đường kính đã sắp xếp tăng dần</returns>
        /// <example>
        /// ParseRange("16-25", [6,8,10,12,14,16,18,20,22,25,28,32]) 
        ///   → [16, 18, 20, 22, 25]
        /// 
        /// ParseRange("18, 22, 25", [...]) 
        ///   → [18, 22, 25]
        /// 
        /// ParseRange("16-20, 28", [...]) 
        ///   → [16, 18, 20, 28]
        /// </example>
        public static List<int> ParseRange(string input, List<int> inventory)
        {
            // Nếu input rỗng, trả về toàn bộ inventory
            if (string.IsNullOrWhiteSpace(input))
                return inventory?.OrderBy(x => x).ToList() ?? new List<int>();

            if (inventory == null || inventory.Count == 0)
                return new List<int>();

            var result = new HashSet<int>();

            // Tách các cụm bằng dấu phẩy, khoảng trắng, hoặc chấm phẩy
            var parts = input.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var rawPart in parts)
            {
                string part = rawPart.Trim();

                if (part.Contains("-"))
                {
                    // Xử lý range: "16-25"
                    var rangeParts = part.Split('-');
                    if (rangeParts.Length == 2
                        && int.TryParse(rangeParts[0].Trim(), out int start)
                        && int.TryParse(rangeParts[1].Trim(), out int end))
                    {
                        // Đảm bảo start <= end
                        if (start > end)
                        {
                            int temp = start; start = end; end = temp;
                        }

                        // Lấy tất cả đường kính trong inventory nằm trong [start, end]
                        foreach (int d in inventory)
                        {
                            if (d >= start && d <= end)
                                result.Add(d);
                        }
                    }
                }
                else
                {
                    // Xử lý số đơn: "18"
                    // Có thể có nhiều số cách nhau bởi khoảng trắng: "18 20 22"
                    var singles = part.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var s in singles)
                    {
                        if (int.TryParse(s.Trim(), out int d))
                        {
                            // Chỉ thêm nếu có trong inventory
                            if (inventory.Contains(d))
                                result.Add(d);
                        }
                    }
                }
            }

            return result.OrderBy(x => x).ToList();
        }

        /// <summary>
        /// Kiểm tra input có hợp lệ không (có parse được ít nhất 1 đường kính)
        /// </summary>
        public static bool IsValidRange(string input, List<int> inventory)
        {
            return ParseRange(input, inventory).Count > 0;
        }

        /// <summary>
        /// Format danh sách đường kính thành string ngắn gọn.
        /// VD: [16, 18, 20, 22, 25] → "16-22, 25" hoặc "16, 18, 20, 22, 25"
        /// </summary>
        public static string FormatRange(List<int> diameters, bool compact = true)
        {
            if (diameters == null || diameters.Count == 0)
                return "";

            if (!compact)
                return string.Join(", ", diameters);

            // Compact: Tìm các chuỗi liên tục
            var sorted = diameters.OrderBy(x => x).ToList();
            var ranges = new List<string>();

            int start = sorted[0];
            int prev = sorted[0];

            for (int i = 1; i < sorted.Count; i++)
            {
                // Kiểm tra có liên tục không (gap <= 4mm thường là liên tục trong TCVN)
                if (sorted[i] - prev <= 4 && sorted[i] - prev > 0)
                {
                    prev = sorted[i];
                }
                else
                {
                    // Kết thúc range
                    ranges.Add(start == prev ? start.ToString() : $"{start}-{prev}");
                    start = sorted[i];
                    prev = sorted[i];
                }
            }

            // Thêm range cuối cùng
            ranges.Add(start == prev ? start.ToString() : $"{start}-{prev}");

            return string.Join(", ", ranges);
        }

        /// <summary>
        /// Lọc danh sách ưu tiên đường kính chẵn (16, 18, 20... thay vì 13, 19...)
        /// </summary>
        public static List<int> FilterEvenDiameters(List<int> diameters)
        {
            return diameters.Where(d => d % 2 == 0).ToList();
        }

        /// <summary>
        /// Lấy đường kính gần nhất trong inventory
        /// </summary>
        public static int GetClosest(int target, List<int> inventory, bool preferLarger = true)
        {
            if (inventory == null || inventory.Count == 0)
                return target;

            var sorted = inventory.OrderBy(x => x).ToList();

            if (preferLarger)
            {
                // Tìm đường kính nhỏ nhất >= target
                foreach (int d in sorted)
                {
                    if (d >= target) return d;
                }
                return sorted.Last(); // Lấy lớn nhất nếu không tìm thấy
            }
            else
            {
                // Tìm đường kính gần nhất
                return sorted.OrderBy(d => Math.Abs(d - target)).First();
            }
        }
    }
}
