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
        /// Parse chuỗi quy tắc auto legs (VD: "250-2 400-3 600-4 800-5")
        /// Trả về list các tuple (maxWidth, legs) đã sắp xếp tăng dần.
        /// </summary>
        public static List<(int, int)> ParseAutoLegsRules(string rules)
        {
            var result = new List<(int, int)>();
            if (string.IsNullOrWhiteSpace(rules)) return result;

            var parts = rules.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var kv = part.Split('-');
                if (kv.Length == 2 && int.TryParse(kv[0], out int w) && int.TryParse(kv[1], out int l))
                {
                    result.Add((w, l)); // Item1 = maxWidth, Item2 = legs
                }
            }
            return result.OrderBy(x => x.Item1).ToList();
        }

        /// <summary>
        /// Tính số nhánh đai tự động dựa trên bề rộng dầm và quy tắc user định nghĩa.
        /// </summary>
        public static int GetAutoLegs(double beamWidthMm, RebarSettings settings)
        {
            if (!settings.AutoLegsFromWidth)
                return settings.StirrupLegs > 0 ? settings.StirrupLegs : 2;

            var rules = ParseAutoLegsRules(settings.AutoLegsRules);
            if (rules.Count == 0)
            {
                // Quy tắc mặc định nếu không có
                if (beamWidthMm <= 250) return 2;
                if (beamWidthMm <= 400) return 3;
                if (beamWidthMm <= 600) return 4;
                return 5;
            }

            // Tìm quy tắc phù hợp (Item1 = maxWidth, Item2 = legs)
            foreach (var rule in rules)
            {
                if (beamWidthMm <= rule.Item1)
                    return rule.Item2;
            }
            // Nếu bề rộng lớn hơn tất cả, dùng số nhánh lớn nhất
            return rules.Last().Item2;
        }

        /// <summary>
        /// Tính toán bước đai từ diện tích cắt và xoắn yêu cầu.
        /// Công thức ACI/TCVN: Atotal/s = Av/s + 2×At/s
        /// Thuật toán vét cạn: thử từng đường kính × từng số nhánh để tìm phương án tối ưu.
        /// Output: String dạng "2-d8a150" (số nhánh - phi - bước)
        /// </summary>
        /// <param name="beamWidthMm">Bề rộng dầm (mm) để tính auto legs. Nếu 0 sẽ dùng StirrupLegs.</param>
        public static string CalculateStirrup(double shearArea, double ttArea, double beamWidthMm, RebarSettings settings)
        {
            // ACI/TCVN: Tổng diện tích đai trên đơn vị dài = Av/s + 2 * At/s
            double totalAreaPerLen = shearArea + 2 * ttArea; 
            
            if (totalAreaPerLen <= 0.001) return "-";

            // Lấy danh sách đường kính đai (ưu tiên nhỏ trước để tiết kiệm)
            var diameters = settings.StirrupDiameters;
            if (diameters == null || diameters.Count == 0)
                diameters = new List<int> { settings.StirrupDiameter > 0 ? settings.StirrupDiameter : 8 };

            var spacings = settings.StirrupSpacings;
            if (spacings == null || spacings.Count == 0)
                spacings = new List<int> { 100, 150, 200, 250 };

            int minSpacingAcceptable = 100;

            // Tính số nhánh cơ sở từ bề rộng dầm (hoặc dùng fixed nếu AutoLegsFromWidth = false)
            int baseLegs = GetAutoLegs(beamWidthMm, settings);

            // Tạo danh sách phương án: baseLegs ± 1, 2 để tìm tối ưu
            var legOptions = new List<int> { baseLegs };
            if (baseLegs - 1 >= 2) legOptions.Insert(0, baseLegs - 1);
            legOptions.Add(baseLegs + 1);
            legOptions.Add(baseLegs + 2);
            
            // Lọc bỏ nhánh lẻ nếu không cho phép
            if (!settings.AllowOddLegs)
                legOptions = legOptions.Where(l => l % 2 == 0).ToList();
            
            if (legOptions.Count == 0)
                legOptions = new List<int> { 2, 4 };

            // Duyệt qua từng đường kính đai (ưu tiên đai nhỏ trước để tiết kiệm)
            foreach (int d in diameters.OrderBy(x => x))
            {
                // Với mỗi đường kính, thử tăng dần số nhánh
                foreach (int legs in legOptions)
                {
                    string res = TryFindSpacing(totalAreaPerLen, d, legs, spacings, minSpacingAcceptable);
                    if (res != null) return res; // Tìm thấy phương án thỏa mãn đầu tiên
                }
            }

            // Nếu vẫn không được, trả về phương án Max (lấy số nhánh lớn nhất trong list thử)
            int maxLegs = legOptions.Last();
            int dMax = diameters.Max();
            int sMin = spacings.Min();
            return $"{maxLegs}-d{dMax}a{sMin}*";
        }

        /// <summary>
        /// Helper: Thử tìm bước đai phù hợp cho đường kính và số nhánh cho trước.
        /// Trả về null nếu không tìm được bước đai >= minSpacingAcceptable.
        /// </summary>
        private static string TryFindSpacing(double totalAreaPerLen, int d, int legs, List<int> spacings, int minSpacingAcceptable)
        {
            double as1Layer = (Math.PI * d * d / 400.0) * legs;
            
            // Tính bước đai max cho phép (mm) = (As_1_layer / Areq_per_cm) * 10
            double maxSpacingReq = (as1Layer / totalAreaPerLen) * 10.0;

            // Tìm bước đai lớn nhất trong list mà vẫn <= maxSpacingReq
            foreach (var s in spacings.OrderByDescending(x => x))
            {
                if (s <= maxSpacingReq && s >= minSpacingAcceptable)
                {
                    return $"{legs}-d{d}a{s}";
                }
            }

            return null; // Không tìm được bước phù hợp
        }

        /// <summary>
        /// Tính toán cốt giá/sườn (Web bars).
        /// Logic: Envelope(Torsion, Constructive) và làm chẵn.
        /// Sử dụng danh sách đường kính để tìm phương án tối ưu.
        /// </summary>
        public static string CalculateWebBars(double torsionTotal, double torsionRatioSide, double heightMm, RebarSettings settings)
        {
            // Lấy danh sách đường kính sườn (ưu tiên nhỏ trước)
            var diameters = settings.WebBarDiameters;
            if (diameters == null || diameters.Count == 0)
                diameters = new List<int> { settings.WebBarDiameter > 0 ? settings.WebBarDiameter : 12 };

            double minHeight = settings.WebBarMinHeight > 0 ? settings.WebBarMinHeight : 700;

            // a. Theo chịu lực xoắn
            double reqArea = torsionTotal * torsionRatioSide;

            // b. Theo cấu tạo (Dầm cao >= minHeight)
            bool needConstructive = heightMm >= minHeight;
            
            // Thử từng đường kính để tìm phương án tối ưu
            foreach (int d in diameters.OrderBy(x => x))
            {
                double as1 = Math.PI * d * d / 400.0;
                
                int nTorsion = 0;
                if (reqArea > 0.01)
                    nTorsion = (int)Math.Ceiling(reqArea / as1);

                int nConstructive = needConstructive ? 2 : 0;

                // Lấy Max và làm chẵn
                int nFinal = Math.Max(nTorsion, nConstructive);
                if (nFinal > 0 && nFinal % 2 != 0) nFinal++;

                if (nFinal > 0 && nFinal <= 6) // Giới hạn hợp lý
                    return $"{nFinal}d{d}";
            }

            // Fallback: dùng đường kính lớn nhất
            int dMax = diameters.Max();
            double asMax = Math.PI * dMax * dMax / 400.0;
            int nMax = reqArea > 0.01 ? (int)Math.Ceiling(reqArea / asMax) : (needConstructive ? 2 : 0);
            if (nMax % 2 != 0) nMax++;
            
            if (nMax == 0) return "-";
            return $"{nMax}d{dMax}";
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
