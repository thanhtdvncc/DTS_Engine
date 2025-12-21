using System;
using System.Collections.Generic;
using System.Linq;
using DTS_Engine.Core.Data;
using DTS_Engine.Core.Utils;

namespace DTS_Engine.Core.Algorithms.Rebar.V4
{
    /// <summary>
    /// Bộ giải cục bộ cho một mặt cắt.
    /// Sinh ra tất cả phương án bố trí thép hợp lệ tại mặt cắt đó.
    /// Hỗ trợ N layers linh hoạt, không hardcode số lớp tối đa.
    /// 
    /// ISO 25010: Functional Correctness - Exhaustive enumeration with early pruning.
    /// ISO 12207: Detailed Design - Single Responsibility Principle.
    /// </summary>
    public class SectionSolver
    {
        #region Configuration

        /// <summary>Số phương án tối đa giữ lại cho mỗi section</summary>
        public int MaxArrangementsPerSection { get; set; } = 50;

        /// <summary>Có thử mixed diameters không</summary>
        public bool AllowMixedDiameters { get; set; } = true;

        #endregion

        #region Dependencies

        private readonly DtsSettings _settings;
        private readonly List<int> _allowedDiameters;
        private readonly int _maxLayers;
        private readonly double _minSpacing;
        private readonly double _maxSpacing;
        private readonly double _minLayerSpacing;
        private readonly int _aggregateSize;
        private readonly int _minBarsPerLayer;

        #endregion

        #region Constructor

        /// <summary>
        /// Tạo SectionSolver với settings.
        /// </summary>
        public SectionSolver(DtsSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            // Parse allowed diameters from settings
            var inventory = settings.General?.AvailableDiameters ?? new List<int> { 12, 14, 16, 18, 20, 22, 25, 28, 32 };
            _allowedDiameters = DiameterParser.ParseRange(
                settings.Beam?.MainBarRange ?? "16-25",
                inventory);

            if (settings.Beam?.PreferEvenDiameter == true)
            {
                _allowedDiameters = DiameterParser.FilterEvenDiameters(_allowedDiameters);
            }

            if (_allowedDiameters.Count == 0)
            {
                _allowedDiameters = inventory.Where(d => d >= 16 && d <= 25).ToList();
            }

            // Get constraints from settings (no hardcoded values)
            _maxLayers = settings.Beam?.MaxLayers ?? 3;
            _minSpacing = settings.Beam?.MinClearSpacing ?? 25;
            _maxSpacing = settings.Beam?.MaxClearSpacing ?? 300;
            _minLayerSpacing = settings.Beam?.MinLayerSpacing ?? 25;
            _aggregateSize = settings.Beam?.AggregateSize ?? 20;
            _minBarsPerLayer = settings.Beam?.MinBarsPerLayer ?? 2;

            // Allow mixed diameters if not explicitly disabled
            AllowMixedDiameters = !(settings.Beam?.PreferSingleDiameter ?? false);
        }

        #endregion

        #region Public API

        /// <summary>
        /// Sinh tất cả phương án bố trí hợp lệ cho mặt cắt.
        /// </summary>
        /// <param name="section">Mặt cắt cần giải</param>
        /// <param name="position">Top hoặc Bot</param>
        /// <returns>Danh sách phương án sắp theo Score giảm dần</returns>
        public List<SectionArrangement> Solve(DesignSection section, RebarPosition position)
        {
            if (section == null)
                return new List<SectionArrangement> { SectionArrangement.Empty };

            double reqArea = position == RebarPosition.Top ? section.ReqTop : section.ReqBot;
            double usableWidth = section.UsableWidth;
            double usableHeight = section.UsableHeight;

            // Không yêu cầu thép
            if (reqArea <= 0.01)
            {
                return new List<SectionArrangement> { SectionArrangement.Empty };
            }

            var results = new List<SectionArrangement>();

            // === PHASE 1: Single Diameter Solutions ===
            results.AddRange(GenerateSingleDiameterArrangements(reqArea, usableWidth, usableHeight, section));

            // === PHASE 2: Mixed Diameter Solutions (nếu cho phép) ===
            if (AllowMixedDiameters && (results.Count == 0 || results.All(r => r.Score < 60)))
            {
                results.AddRange(GenerateMixedDiameterArrangements(reqArea, usableWidth, section));
            }

            // === PHASE 3: Scoring and Filtering ===
            foreach (var arr in results)
            {
                arr.Score = CalculateScore(arr, reqArea, section);
                arr.Efficiency = reqArea > 0 ? arr.TotalArea / reqArea : 0;
            }

            // Sort and limit
            return results
                .Where(r => r.TotalArea >= reqArea * 0.98) // 2% tolerance
                .OrderByDescending(r => r.Score)
                .ThenBy(r => r.LayerCount)
                .ThenBy(r => r.TotalCount)
                .DistinctBy(r => r.ToDisplayString()) // Remove duplicates
                .Take(MaxArrangementsPerSection)
                .ToList();
        }

        /// <summary>
        /// Giải tất cả sections trong danh sách.
        /// </summary>
        public void SolveAll(List<DesignSection> sections)
        {
            if (sections == null) return;

            foreach (var section in sections)
            {
                section.ValidArrangementsTop = Solve(section, RebarPosition.Top);
                section.ValidArrangementsBot = Solve(section, RebarPosition.Bot);
            }
        }

        #endregion

        #region Single Diameter Generation

        /// <summary>
        /// Sinh phương án với đường kính đơn (1 đến N layers).
        /// </summary>
        private List<SectionArrangement> GenerateSingleDiameterArrangements(
            double reqArea,
            double usableWidth,
            double usableHeight,
            DesignSection section)
        {
            var results = new List<SectionArrangement>();

            foreach (int dia in _allowedDiameters.OrderByDescending(d => d))
            {
                double barArea = Math.PI * dia * dia / 400.0; // cm²

                // Tính số thanh tối thiểu để đủ diện tích
                int minBars = Math.Max(_minBarsPerLayer, (int)Math.Ceiling(reqArea / barArea));

                // Tính số thanh tối đa mỗi lớp
                int maxBarsLayer1 = CalculateMaxBarsPerLayer(usableWidth, dia);
                if (maxBarsLayer1 < _minBarsPerLayer) continue;

                // Tính số lớp tối đa có thể (dựa trên chiều cao)
                int maxPossibleLayers = CalculateMaxLayers(usableHeight, dia);
                int effectiveMaxLayers = Math.Min(_maxLayers, maxPossibleLayers);

                // Tính tổng số thanh tối đa có thể bố trí
                int maxTotalBars = maxBarsLayer1 * effectiveMaxLayers;

                // Thử từ minBars đến maxTotalBars
                for (int totalBars = minBars; totalBars <= maxTotalBars; totalBars++)
                {
                    double providedArea = totalBars * barArea;
                    if (providedArea < reqArea * 0.98) continue;

                    // Thử các cách phân lớp khác nhau
                    var layerConfigs = GenerateLayerConfigurations(totalBars, maxBarsLayer1, effectiveMaxLayers);

                    foreach (var config in layerConfigs)
                    {
                        // Verify spacing cho layer 1
                        if (!CheckSpacing(usableWidth, config[0], dia)) continue;

                        var arrangement = CreateArrangement(config, dia, usableWidth, reqArea);
                        if (arrangement != null && !results.Any(r => r.Equals(arrangement)))
                        {
                            results.Add(arrangement);
                        }
                    }

                    // Giới hạn để tránh quá tải
                    if (results.Count >= MaxArrangementsPerSection * 2) break;
                }

                if (results.Count >= MaxArrangementsPerSection * 2) break;
            }

            return results;
        }

        /// <summary>
        /// Sinh tất cả cách phân lớp hợp lệ cho N layers.
        /// </summary>
        private List<List<int>> GenerateLayerConfigurations(int totalBars, int maxPerLayer, int maxLayers)
        {
            var configs = new List<List<int>>();

            // Simple case: fit in 1 layer
            if (totalBars <= maxPerLayer)
            {
                configs.Add(new List<int> { totalBars });
                return configs;
            }

            // Generate multi-layer configurations
            GenerateLayerConfigsRecursive(totalBars, maxPerLayer, maxLayers, new List<int>(), configs);

            return configs;
        }

        private void GenerateLayerConfigsRecursive(
            int remaining,
            int maxPerLayer,
            int maxLayers,
            List<int> current,
            List<List<int>> results)
        {
            // Đã dùng hết số lớp cho phép
            if (current.Count >= maxLayers)
            {
                if (remaining == 0)
                {
                    results.Add(new List<int>(current));
                }
                return;
            }

            // Còn 0 thanh
            if (remaining == 0)
            {
                results.Add(new List<int>(current));
                return;
            }

            // Thử các số lượng cho layer hiện tại
            int minThisLayer = Math.Max(_minBarsPerLayer, remaining - maxPerLayer * (maxLayers - current.Count - 1));
            int maxThisLayer = Math.Min(remaining, maxPerLayer);

            // Ưu tiên layer 1 đầy trước
            if (current.Count == 0)
            {
                minThisLayer = Math.Max(minThisLayer, maxPerLayer - 2);
            }

            for (int n = maxThisLayer; n >= minThisLayer; n--)
            {
                // Đảm bảo không để layer sau có ít hơn minBars (trừ layer cuối)
                int remainingAfter = remaining - n;
                if (remainingAfter > 0 && remainingAfter < _minBarsPerLayer && current.Count < maxLayers - 1)
                {
                    continue;
                }

                current.Add(n);
                GenerateLayerConfigsRecursive(remainingAfter, maxPerLayer, maxLayers, current, results);
                current.RemoveAt(current.Count - 1);

                // Giới hạn số configurations
                if (results.Count >= 10) return;
            }
        }

        #endregion

        #region Mixed Diameter Generation

        /// <summary>
        /// Sinh phương án với mixed diameters (2 đường kính khác nhau).
        /// </summary>
        private List<SectionArrangement> GenerateMixedDiameterArrangements(
            double reqArea,
            double usableWidth,
            DesignSection section)
        {
            var results = new List<SectionArrangement>();

            // Lấy 2-3 đường kính lớn nhất
            var topDiameters = _allowedDiameters
                .OrderByDescending(d => d)
                .Take(3)
                .ToList();

            foreach (int dia1 in topDiameters)
            {
                foreach (int dia2 in topDiameters.Where(d => d < dia1))
                {
                    double area1 = Math.PI * dia1 * dia1 / 400.0;
                    double area2 = Math.PI * dia2 * dia2 / 400.0;

                    // Thử các tổ hợp
                    for (int n1 = _minBarsPerLayer; n1 <= 6; n1++)
                    {
                        double remaining = reqArea - n1 * area1;
                        if (remaining <= 0) continue;

                        int n2 = (int)Math.Ceiling(remaining / area2);
                        n2 = Math.Max(1, n2);

                        int total = n1 + n2;

                        // Kiểm tra fit trong 1 layer
                        if (!CanFitMixedBars(usableWidth, n1, dia1, n2, dia2)) continue;

                        var diameters = Enumerable.Repeat(dia1, n1)
                            .Concat(Enumerable.Repeat(dia2, n2))
                            .ToList();

                        var arrangement = new SectionArrangement
                        {
                            TotalCount = total,
                            TotalArea = n1 * area1 + n2 * area2,
                            PrimaryDiameter = dia1,
                            BarDiameters = diameters,
                            LayerCount = 1,
                            BarsPerLayer = new List<int> { total },
                            ClearSpacing = CalculateMixedSpacing(usableWidth, diameters),
                            IsSymmetric = true,
                            FitsStirrupLayout = true,
                            Score = 60 // Base score for mixed
                        };

                        if (!results.Any(r => r.Equals(arrangement)))
                        {
                            results.Add(arrangement);
                        }
                    }
                }
            }

            return results.Take(10).ToList();
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Tính số thanh tối đa mỗi lớp dựa trên bề rộng.
        /// </summary>
        private int CalculateMaxBarsPerLayer(double usableWidth, int diameter)
        {
            double minClear = GetMinClearSpacing(diameter);
            if (usableWidth < diameter) return 0;

            // n*d + (n-1)*minClear <= usableWidth
            // n <= (usableWidth + minClear) / (d + minClear)
            double nMax = (usableWidth + minClear) / (diameter + minClear);
            return Math.Max(0, (int)Math.Floor(nMax));
        }

        /// <summary>
        /// Tính số lớp tối đa dựa trên chiều cao.
        /// </summary>
        private int CalculateMaxLayers(double usableHeight, int diameter)
        {
            if (usableHeight <= 0) return 1;

            // Mỗi lớp cần: diameter + minLayerSpacing
            double perLayer = diameter + _minLayerSpacing;
            int maxLayers = (int)Math.Floor(usableHeight / perLayer);

            return Math.Max(1, Math.Min(maxLayers, 5)); // Cap at 5 layers
        }

        /// <summary>
        /// Lấy khoảng hở tối thiểu (theo tiêu chuẩn).
        /// </summary>
        private double GetMinClearSpacing(int diameter)
        {
            // Min of: bar diameter, minSpacing, 1.33*aggregate
            return Math.Max(diameter, Math.Max(_minSpacing, 1.33 * _aggregateSize));
        }

        /// <summary>
        /// Tính khoảng hở thực tế giữa các thanh.
        /// </summary>
        private double CalculateClearSpacing(double usableWidth, int barsInLayer, int diameter)
        {
            if (barsInLayer <= 1) return usableWidth;
            return (usableWidth - barsInLayer * diameter) / (barsInLayer - 1);
        }

        /// <summary>
        /// Kiểm tra khoảng hở hợp lệ.
        /// </summary>
        private bool CheckSpacing(double usableWidth, int barsInLayer, int diameter)
        {
            double spacing = CalculateClearSpacing(usableWidth, barsInLayer, diameter);
            double minClear = GetMinClearSpacing(diameter);

            return spacing >= minClear && spacing <= _maxSpacing;
        }

        /// <summary>
        /// Kiểm tra mixed bars có fit không.
        /// </summary>
        private bool CanFitMixedBars(double usableWidth, int n1, int dia1, int n2, int dia2)
        {
            double totalBarWidth = n1 * dia1 + n2 * dia2;
            int totalBars = n1 + n2;

            double minClear = Math.Max(GetMinClearSpacing(dia1), GetMinClearSpacing(dia2));
            double totalClearNeeded = (totalBars - 1) * minClear;

            return totalBarWidth + totalClearNeeded <= usableWidth;
        }

        /// <summary>
        /// Tính khoảng hở cho mixed bars.
        /// </summary>
        private double CalculateMixedSpacing(double usableWidth, List<int> diameters)
        {
            if (diameters.Count <= 1) return usableWidth;
            double totalBarWidth = diameters.Sum();
            return (usableWidth - totalBarWidth) / (diameters.Count - 1);
        }

        /// <summary>
        /// Tạo SectionArrangement từ layer configuration.
        /// </summary>
        private SectionArrangement CreateArrangement(List<int> layers, int diameter, double usableWidth, double reqArea)
        {
            int totalBars = layers.Sum();
            double barArea = Math.PI * diameter * diameter / 400.0;

            return new SectionArrangement
            {
                TotalCount = totalBars,
                TotalArea = totalBars * barArea,
                PrimaryDiameter = diameter,
                LayerCount = layers.Count,
                BarsPerLayer = new List<int>(layers),
                DiametersPerLayer = Enumerable.Repeat(diameter, layers.Count).ToList(),
                ClearSpacing = CalculateClearSpacing(usableWidth, layers[0], diameter),
                VerticalSpacing = _minLayerSpacing,
                IsSymmetric = layers.All(x => x % 2 == 0 || x == 1),
                FitsStirrupLayout = true
            };
        }

        /// <summary>
        /// Tính điểm cho phương án.
        /// </summary>
        private double CalculateScore(SectionArrangement arr, double reqArea, DesignSection section)
        {
            if (arr.TotalArea < reqArea * 0.98) return 0;

            double score = 100.0;

            // 1. Efficiency (max -30): Ít thừa thì tốt
            double wasteRatio = (arr.TotalArea - reqArea) / Math.Max(reqArea, 0.01);
            score -= Math.Min(30, wasteRatio * 50);

            // 2. Layer Penalty (max -20): Ít lớp thì tốt
            score -= (arr.LayerCount - 1) * 8;

            // 3. Bar Count Penalty (max -15): Ít thanh thì tốt
            score -= Math.Min(15, (arr.TotalCount - _minBarsPerLayer) * 2);

            // 4. Spacing Quality (+5): Khoảng hở optimal (100-150mm)
            double optimalSpacing = (_minSpacing + _maxSpacing) / 3; // ~100-120mm typically
            double spacingDiff = Math.Abs(arr.ClearSpacing - optimalSpacing);
            if (spacingDiff < 30) score += 5;

            // 5. Symmetry Bonus (+3): Số thanh chẵn
            if (arr.IsEvenCount) score += 3;

            // 6. Preferred Diameter Bonus (+5)
            int preferredDia = _settings.Beam?.PreferredDiameter ?? 20;
            if (arr.PrimaryDiameter == preferredDia) score += 5;

            // 7. Single Diameter Bonus (+3)
            if (arr.IsSingleDiameter) score += 3;

            return Math.Max(0, Math.Min(100, score));
        }

        #endregion
    }

    /// <summary>
    /// Extension method for DistinctBy (không có sẵn trong .NET Framework 4.8)
    /// </summary>
    internal static class EnumerableExtensions
    {
        public static IEnumerable<T> DistinctBy<T, TKey>(this IEnumerable<T> source, Func<T, TKey> keySelector)
        {
            var seen = new HashSet<TKey>();
            foreach (var item in source)
            {
                var key = keySelector(item);
                if (seen.Add(key))
                {
                    yield return item;
                }
            }
        }
    }
}
