using DTS_Engine.Core.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DTS_Engine.Core.Algorithms
{
    public class RebarCalculator
    {
        /// <summary>
        /// Tính toán chọn thép cho 1 tiết diện.
        /// </summary>
        public static string Calculate(double areaReq, double b, double h, RebarSettings settings)
        {
            if (areaReq <= 0.01) return "-"; // Không cần thép

            // 1. Xác định list đường kính
            var diameters = settings.PreferredDiameters;
            if (diameters == null || diameters.Count == 0) diameters = new List<int> { 16, 18, 20, 22, 25 };

            // 2. Loop qua các đường kính để tìm phương án tối ưu
            // Tiêu chí tối ưu:
            // - Thỏa mãn As >= AreaReq
            // - Thỏa mãn khoảng hở (min spacing)
            // - Ít thanh nhất hoặc dư ít nhất? (Thường ưu tiên số thanh chẵn/hợp lý và dư ít nhất)

            // Chiến thuật "Vét cạn" đơn giản hóa (Greedy per diameter):
            // Thử từng D, xem cần bao nhiêu thanh. Check spacing. Nếu 1 lớp không đủ -> 2 lớp.

            string bestSol = "";
            double minAreaExcess = double.MaxValue;

            foreach (int d in diameters)
            {
                double as1 = Math.PI * d * d / 400.0; // cm2 per bar

                // Số thanh lý thuyết
                int nTotal = (int)Math.Ceiling(areaReq / as1);

                // Check Max thanh 1 lớp
                int nMaxOneLayer = GetMaxBarsPerLayer(b, settings.CoverTop, d, settings.MinSpacing);

                // Xử lý bố trí
                string currentSol = "";

                if (nTotal <= nMaxOneLayer)
                {
                    // 1 Lớp đủ
                    // Quy tắc VBA cũ: Nếu chỉ 1 cây -> ép lên 2 cây (để tạo khung)
                    if (nTotal < 2) nTotal = 2;
                    currentSol = $"{nTotal}d{d}";
                }
                else
                {
                    // Phải 2 lớp
                    // Lớp 1: nMax (hoặc n_chaysuot)
                    // Lớp 2: nTotal - nMax
                    // Ràng buộc số lớp tối đa? User settings usually imply limit.
                    // Let's assume max 2 layers for simplicity first version.

                    int nL1 = nMaxOneLayer;
                    int nL2 = nTotal - nL1;

                    // Logic VBA: n_chaysuot (Run-through).
                    // Thường lớp 1 là chạy suốt, lớp 2 gia cường.
                    // Nếu nL2 quá ít (1 cây), có thể tăng nL2 lên 2.
                    if (nL2 < 2) nL2 = 2;

                    // Re-check total area with adjusted counts
                    nTotal = nL1 + nL2;

                    currentSol = $"{nL1}d{d} + {nL2}d{d}";
                }

                double areaProv = nTotal * as1;
                double excess = areaProv - areaReq;

                // Chọn phương án dư ít nhất (Economy)
                if (excess >= 0 && excess < minAreaExcess)
                {
                    minAreaExcess = excess;
                    bestSol = currentSol;
                }
                else if (string.IsNullOrEmpty(bestSol) && excess >= 0)
                {
                    // Fallback: nếu chưa có sol nào và excess >= 0, lấy luôn
                    bestSol = currentSol;
                    minAreaExcess = excess;
                }
            }

            // Nếu vẫn không tìm được (tất cả diameter đều không đủ chỗ), 
            // dùng đường kính lớn nhất và bố trí dư
            if (string.IsNullOrEmpty(bestSol))
            {
                int dMax = diameters.Max();
                double as1 = Math.PI * dMax * dMax / 400.0;
                int n = (int)Math.Ceiling(areaReq / as1);
                if (n < 2) n = 2;
                bestSol = $"{n}d{dMax}*"; // Asterisk indicates forced arrangement
            }

            return bestSol;
        }

        private static int GetMaxBarsPerLayer(double b, double cover, int d, double minSpacing)
        {
            // b: width (mm)
            // cover: (mm)
            // d: (mm)
            // space: (mm)

            // Valid width = b - 2*cover - 2*stirrup (assume 10mm stirrup)
            double workingWidth = b - 2 * cover - 2 * 10;

            // n * d + (n-1)*s <= workingWidth
            // n(d+s) - s <= workingWidth
            // n(d+s) <= workingWidth + s
            // n <= (workingWidth + s) / (d + s)

            double val = (workingWidth + minSpacing) / (d + minSpacing);
            int n = (int)Math.Floor(val);
            return n < 2 ? 2 : n; // Min 2 bars usually
        }

        /// <summary>
        /// Tính toán bước đai từ diện tích cắt và xoắn yêu cầu.
        /// Công thức ACI/TCVN: Atotal/s = Av/s + 2×At/s
        /// Output: String dạng "2-d8a150" (số nhánh - phi - bước)
        /// </summary>
        public static string CalculateStirrup(double shearArea, double ttArea, RebarSettings settings)
        {
            // ACI/TCVN: Tổng diện tích đai trên đơn vị dài = Av/s + 2 * At/s
            double totalAreaPerLen = shearArea + 2 * ttArea; 
            
            if (totalAreaPerLen <= 0.001) return "-";

            int d = settings.StirrupDiameter;
            var spacings = settings.StirrupSpacings;
            int minSpacing = 100;

            if (spacings == null || spacings.Count == 0)
                spacings = new List<int> { 100, 150, 200, 250 };

            int nLegs = settings.StirrupLegs; 
            double as1Layer = (Math.PI * d * d / 400.0) * nLegs;

            // Tính bước đai max (mm)
            double maxSpacingReq = (as1Layer / totalAreaPerLen) * 10.0;

            int selectedSpacing = -1;
            foreach (var s in spacings.OrderByDescending(x => x))
            {
                if (s <= maxSpacingReq)
                {
                    selectedSpacing = s;
                    break;
                }
            }

            // Nếu bước đai tính ra nhỏ hơn minSpacing (100), trả về bước nhỏ nhất + dấu *
            if (selectedSpacing < minSpacing)
                return $"{nLegs}-d{d}a{spacings.Min()}*";

            return $"{nLegs}-d{d}a{selectedSpacing}";
        }

        /// <summary>
        /// Tính toán cốt giá/sườn (Web bars).
        /// Logic: Envelope(Torsion, Constructive) và làm chẵn.
        /// </summary>
        public static string CalculateWebBars(double torsionTotal, double torsionRatioSide, double heightMm, RebarSettings settings)
        {
            int d = settings.WebBarDiameter;
            double as1 = Math.PI * d * d / 400.0;

            // a. Theo chịu lực xoắn
            double reqArea = torsionTotal * torsionRatioSide;
            int nTorsion = 0;
            if (reqArea > 0.01)
                nTorsion = (int)Math.Ceiling(reqArea / as1);

            // b. Theo cấu tạo (Dầm cao >= 700mm)
            int nConstructive = 0;
            if (heightMm >= 700) nConstructive = 2;

            // c. Lấy Max và làm chẵn
            int nFinal = Math.Max(nTorsion, nConstructive);
            if (nFinal % 2 != 0) nFinal++;

            if (nFinal == 0) return "-";
            return $"{nFinal}d{d}";
        }

        /// <summary>
        /// Parse diện tích thép từ chuỗi bố trí dọc (VD: "4d20", "2d16+3d18").
        /// Trả về tổng diện tích cm2.
        /// </summary>
        public static double ParseRebarArea(string rebarStr)
        {
            if (string.IsNullOrEmpty(rebarStr) || rebarStr == "-") return 0;

            double total = 0;
            // Split by '+' for multi-layer arrangements
            var parts = rebarStr.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var s = part.Trim();
                // Expected format: NdD (e.g., "4d20")
                var match = System.Text.RegularExpressions.Regex.Match(s, @"(\d+)[dD](\d+)");
                if (match.Success)
                {
                    int n = int.Parse(match.Groups[1].Value);
                    int d = int.Parse(match.Groups[2].Value);
                    double as1 = Math.PI * d * d / 400.0; // cm2 per bar
                    total += n * as1;
                }
            }
            return total;
        }

        /// <summary>
        /// Parse diện tích đai trên đơn vị dài từ chuỗi bố trí (VD: "d10a150", "4-d8a100").
        /// Trả về A/s (cm2/cm). Tự động nhận diện số nhánh nếu có tiền tố "N-".
        /// </summary>
        public static double ParseStirrupAreaPerLen(string stirrupStr, int defaultLegs = 2)
        {
            if (string.IsNullOrEmpty(stirrupStr) || stirrupStr == "-") return 0;

            // Regex bắt cả số nhánh (Group 1 - Optional)
            // Format: "4-d8a100" hoặc "d8a150"
            var match = System.Text.RegularExpressions.Regex.Match(stirrupStr, @"(?:(\d+)-)?[dD](\d+)[aA](\d+)");
            
            if (match.Success)
            {
                // 1. Xác định số nhánh
                int nLegs = defaultLegs;
                if (match.Groups[1].Success && int.TryParse(match.Groups[1].Value, out int n))
                {
                    nLegs = n;
                }

                // 2. Lấy đường kính và bước
                int d = int.Parse(match.Groups[2].Value);
                int spacing = int.Parse(match.Groups[3].Value); // mm
                
                if (spacing <= 0) return 0;

                double as1 = Math.PI * d * d / 400.0; // cm2 per bar
                
                // 3. Tính cm2/cm: (nLegs * As1) / (Spacing_cm)
                double areaPerLen = (nLegs * as1) / (spacing / 10.0); 
                return areaPerLen;
            }
            return 0;
        }
    }
}
