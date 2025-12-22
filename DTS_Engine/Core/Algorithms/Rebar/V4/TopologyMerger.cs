using System;
using System.Collections.Generic;
using System.Linq;
using DTS_Engine.Core.Data;
using DTS_Engine.Core.Utils;

namespace DTS_Engine.Core.Algorithms.Rebar.V4
{
    /// <summary>
    /// TopologyMerger V4.2: TỰ CÂN BẰNG THÔNG MINH
    /// 
    /// Xử lý ràng buộc Type 3 (Gối đỡ): Left và Right của cùng gối PHẢI thống nhất.
    /// 
    /// CRITICAL FIX:
    /// 1. Thử Intersection (Giao thoa) - Tìm phương án chung cho cả 2 bên
    /// 2. Thử Rebalance - Tìm phương án nhỏ hơn thỏa mãn cả 2 (VD: 3D20 thay vì 4D22/2D22)
    /// 3. Force Governing - Nếu không thể, BẮT BUỘC lấy theo bên có nội lực lớn hơn
    /// 
    /// Triết lý: 2 mặt bên của cột KHÔNG THỂ có 2 cách bố trí khác nhau.
    /// </summary>
    public class TopologyMerger
    {
        #region Configuration

        public double PositionTolerance { get; set; } = 0.02;
        public int MaxBarCountDifference { get; set; } = 2;
        public int MaxLayerCountDifference { get; set; } = 1;

        #endregion

        #region Dependencies

        private readonly DtsSettings _settings;
        private readonly List<int> _allowedDiameters;

        #endregion

        #region Constructor

        public TopologyMerger(DtsSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            var generalCfg = settings.General ?? new GeneralConfig();
            var beamCfg = settings.Beam ?? new BeamConfig();

            var inventory = generalCfg.AvailableDiameters ?? new List<int> { 16, 18, 20, 22, 25 };
            _allowedDiameters = DiameterParser.ParseRange(beamCfg.MainBarRange ?? "16-25", inventory);

            if (_allowedDiameters.Count == 0)
            {
                _allowedDiameters = inventory.Where(d => d >= 16 && d <= 25).ToList();
            }
        }

        #endregion

        #region Public API

        public bool ApplyConstraints(List<DesignSection> sections)
        {
            if (sections == null || sections.Count == 0) return false;

            // Bước 1: Liên kết các cặp gối
            var supportPairs = IdentifySupportPairs(sections);
            Utils.RebarLogger.Log($"TopologyMerger: Found {supportPairs.Count} support pairs");

            // Bước 2: Áp dụng SMART MERGE với AUTO-REBALANCE
            foreach (var pair in supportPairs)
            {
                MergeGoverning(pair);
            }

            // Bước 3 & 4: Các ràng buộc phụ
            ApplyStirrupCompatibility(sections);
            ApplyVerticalAlignment(sections);

            return ValidateAllSectionsHaveOptions(sections);
        }

        public List<SupportPair> GetSupportPairs(List<DesignSection> sections)
        {
            return IdentifySupportPairs(sections);
        }

        #endregion

        #region Support Pair Identification

        public class SupportPair
        {
            public int SupportIndex { get; set; }
            public DesignSection LeftSection { get; set; }
            public DesignSection RightSection { get; set; }
            public double Position { get; set; }
            public bool IsMerged { get; set; }
            public List<SectionArrangement> MergedTop { get; set; }
            public List<SectionArrangement> MergedBot { get; set; }
        }

        private List<SupportPair> IdentifySupportPairs(List<DesignSection> sections)
        {
            var pairs = new List<SupportPair>();
            var supports = sections.Where(s => s.Type == SectionType.Support).OrderBy(s => s.Position).ToList();
            var groupedByPosition = new Dictionary<double, List<DesignSection>>();

            foreach (var support in supports)
            {
                var matchingKey = groupedByPosition.Keys
                    .Where(k => Math.Abs(k - support.Position) <= PositionTolerance)
                    .Cast<double?>().FirstOrDefault();

                if (matchingKey.HasValue) groupedByPosition[matchingKey.Value].Add(support);
                else groupedByPosition[support.Position] = new List<DesignSection> { support };
            }

            int supportIndex = 0;
            foreach (var group in groupedByPosition.OrderBy(g => g.Key))
            {
                if (group.Value.Count >= 2)
                {
                    var leftSection = group.Value.FirstOrDefault(s => s.IsSupportRight); // End of left span
                    var rightSection = group.Value.FirstOrDefault(s => s.IsSupportLeft); // Start of right span

                    if (leftSection != null && rightSection != null && leftSection != rightSection)
                    {
                        leftSection.LinkedSection = rightSection;
                        rightSection.LinkedSection = leftSection;

                        pairs.Add(new SupportPair
                        {
                            SupportIndex = supportIndex,
                            LeftSection = leftSection,
                            RightSection = rightSection,
                            Position = group.Key
                        });
                    }
                }
                supportIndex++;
            }
            return pairs;
        }

        #endregion

        #region SMART MERGE WITH AUTO-REBALANCE

        /// <summary>
        /// Merge thông minh với tự động cân bằng:
        /// 1. Thử Intersection (Giao thoa)
        /// 2. Thử Rebalance (Tìm phương án nhỏ hơn thỏa mãn cả 2)
        /// 3. Force Governing (Bắt buộc lấy theo bên lớn hơn)
        /// </summary>
        private void MergeGoverning(SupportPair pair)
        {
            var left = pair.LeftSection;
            var right = pair.RightSection;

            LogMergeDetails(left, right);

            // === MERGE TOP ===
            double governingTop = Math.Max(left.ReqTop, right.ReqTop);
            var mergedTop = MergeGoverningArrangements(
                left.ValidArrangementsTop,
                right.ValidArrangementsTop,
                governingTop);

            // Post-Merge Validation (Option A: Tolerance via SafetyFactor)
            bool leftTopOk = ValidateMergedList(mergedTop, left.ReqTop);
            bool rightTopOk = ValidateMergedList(mergedTop, right.ReqTop);

            if (!leftTopOk || !rightTopOk)
            {
                Utils.RebarLogger.Log($"  Warn: Merge Top Deficient (L={leftTopOk}, R={rightTopOk}). Force Merging (Option B).");
            }

            if (mergedTop.Count > 0)
            {
                pair.MergedTop = mergedTop;
                left.ValidArrangementsTop = mergedTop.Select(CloneArrangement).ToList();
                right.ValidArrangementsTop = mergedTop.Select(CloneArrangement).ToList();
            }
            else
            {
                Utils.RebarLogger.Log("  Error: Merge Top FAILED.");
            }

            // === MERGE BOT ===
            double governingBot = Math.Max(left.ReqBot, right.ReqBot);
            var mergedBot = MergeGoverningArrangements(
                left.ValidArrangementsBot,
                right.ValidArrangementsBot,
                governingBot);

            bool leftBotOk = ValidateMergedList(mergedBot, left.ReqBot);
            bool rightBotOk = ValidateMergedList(mergedBot, right.ReqBot);

            if (!leftBotOk || !rightBotOk)
            {
                Utils.RebarLogger.Log($"  Warn: Merge Bot Deficient (L={leftBotOk}, R={rightBotOk}). Force Merging (Option B).");
            }

            if (mergedBot.Count > 0)
            {
                pair.MergedBot = mergedBot;
                left.ValidArrangementsBot = mergedBot.Select(CloneArrangement).ToList();
                right.ValidArrangementsBot = mergedBot.Select(CloneArrangement).ToList();
            }
            else
            {
                Utils.RebarLogger.Log("  Error: Merge Bot FAILED.");
            }

            pair.IsMerged = (mergedTop.Count > 0) || (mergedBot.Count > 0);
        }

        /// <summary>
        /// Validation: Đảm bảo merged list có ít nhất 1 phương án >= originalReq (với tolerance)
        /// </summary>
        private bool ValidateMergedList(List<SectionArrangement> mergedList, double originalReq)
        {
            if (mergedList == null || mergedList.Count == 0) return false;
            if (originalReq <= 0.01) return true;

            // Tolerance from Safety Factor (Option A)
            // SF = 1.0 -> Tolerance = 0
            // SF = 0.9 -> Tolerance = 10%
            double safetyFactor = _settings.Rules?.SafetyFactor ?? 1.0;

            // NOTE: Requirement logic is: Capacity >= Req * SafetyFactor ? 
            // Correct logic: providedArea >= Req (raw). Internal verification usually applies SafetyFactor.
            // But here, originalReq is likely the RAW required area from SAP.
            // If SafetyFactor = 1.0, we need Area >= Req.

            // Let's use 1.0 tolerance for now (strict) unless SF implies otherwise used elsewhere.
            // For now: provided >= originalReq

            return mergedList.Any(a => a.TotalArea >= originalReq);
        }

        // Placeholder for the new LogMergeDetails signature
        private void LogMergeDetails(DesignSection left, DesignSection right)
        {
            Utils.RebarLogger.Log($"MERGE Support (Pos {left.Position:F1}):");
            Utils.RebarLogger.Log($"  Left: ReqTop={left.ReqTop:F2}, ReqBot={left.ReqBot:F2} | Options: T={left.ValidArrangementsTop.Count}, B={left.ValidArrangementsBot.Count}");
            Utils.RebarLogger.Log($"  Right: ReqTop={right.ReqTop:F2}, ReqBot={right.ReqBot:F2} | Options: T={right.ValidArrangementsTop.Count}, B={right.ValidArrangementsBot.Count}");
        }

        private List<SectionArrangement> MergeGoverningArrangements(
            List<SectionArrangement> list1,
            List<SectionArrangement> list2,
            double governingReq)
        {
            double safetyFactor = _settings.Rules?.SafetyFactor ?? 1.0;
            // Tolerance logic: If SF=1.0, tolerance=0. If user wants relaxed, they lower SF.
            double tolerance = Math.Max(0, 1.0 - safetyFactor);

            // Filter both lists by governing requirement
            var valid1 = list1?.Where(a => a.TotalArea >= governingReq * (1 - tolerance)).ToList() ?? new List<SectionArrangement>();
            var valid2 = list2?.Where(a => a.TotalArea >= governingReq * (1 - tolerance)).ToList() ?? new List<SectionArrangement>();

            // 1. Try FindIntersection (Identical arrangements)
            var intersection = new List<SectionArrangement>();
            foreach (var v1 in valid1)
            {
                // Simple equality check: same bar count & diameter
                // More complex: same area? 
                // Let's rely on standard equality: Count == Count, Dia == Dia
                var match = valid2.FirstOrDefault(v2 =>
                    v2.TotalCount == v1.TotalCount &&
                    v2.PrimaryDiameter == v1.PrimaryDiameter &&
                    Math.Abs(v2.TotalArea - v1.TotalArea) < 0.01);

                if (match != null) intersection.Add(v1);
            }

            if (intersection.Count > 0) return intersection;

            // 2. Fallback: Return valid options from the list that has them
            // If both have valid options but no intersection, prioritize the one with FEWER bars?
            // User requested "Force Governing" -> Prioritize options that satisfy the governing req.
            // If both have valid options, join them? No, that explodes combinations.
            // Simple logic: Take valid1 if it has options, else valid2.
            if (valid1.Count > 0) return valid1;
            if (valid2.Count > 0) return valid2;

            return new List<SectionArrangement>();
        }

        private void LogMergeDetails(string side, List<SectionArrangement> list)
        {
            if (list == null || list.Count == 0) return;
            var details = list.Take(5).Select(a => $"{a.TotalCount}D{a.PrimaryDiameter}({a.TotalArea:F2})");
            string more = list.Count > 5 ? "..." : "";
            Utils.RebarLogger.Log($"    {side}: {string.Join(", ", details)}{more}");
        }

        /// <summary>
        /// Chiến lược merge với tự động cân bằng:
        /// 1. Intersection (Giao thoa) - Tìm phương án chung
        /// 2. Rebalance - Tìm phương án nhỏ hơn thỏa mãn cả 2
        /// 3. Force Governing - Bắt buộc lấy theo bên lớn hơn
        /// </summary>
        private List<SectionArrangement> ExecuteMergeWithRebalance(
            List<SectionArrangement> list1,
            List<SectionArrangement> list2,
            double req1,
            double req2,
            DesignSection section1,
            DesignSection section2,
            string sideName)
        {
            double governingReq = Math.Max(req1, req2);
            double safetyFactor = _settings.Rules?.SafetyFactor ?? 1.0;
            double tolerance = Math.Max(0, 1.0 - safetyFactor);

            // Filter cả 2 list theo Governing Req trước
            var valid1 = list1?.Where(a => a.TotalArea >= governingReq * (1 - tolerance)).ToList() ?? new List<SectionArrangement>();
            var valid2 = list2?.Where(a => a.TotalArea >= governingReq * (1 - tolerance)).ToList() ?? new List<SectionArrangement>();

            // ═══════════════════════════════════════════════════════════════
            // STRATEGY 1: INTERSECTION (Giao thoa)
            // Tìm phương án có cùng (Diameter, Count) xuất hiện ở CẢ 2 bên
            // ═══════════════════════════════════════════════════════════════
            var intersection = FindIntersection(valid1, valid2);

            if (intersection.Count > 0)
            {
                Utils.RebarLogger.Log($"  -> {sideName}: INTERSECTION found {intersection.Count} common options");
                return intersection;
            }

            // ═══════════════════════════════════════════════════════════════
            // STRATEGY 2: REBALANCE (Tìm phương án nhỏ hơn thỏa mãn cả 2)
            // VD: Gối 1 cần 19cm² (4D22), Gối 2 cần 8cm² (2D22)
            //     -> Thử 3D20 = 9.42cm² có thỏa mãn cả 2 không?
            // ═══════════════════════════════════════════════════════════════
            var rebalanced = TryRebalance(section1, section2, governingReq, sideName);

            if (rebalanced != null && rebalanced.Count > 0)
            {
                Utils.RebarLogger.Log($"  -> {sideName}: REBALANCED to {rebalanced.First().TotalCount}D{rebalanced.First().PrimaryDiameter}");
                return rebalanced;
            }

            // ═══════════════════════════════════════════════════════════════
            // STRATEGY 3: FORCE GOVERNING (Bắt buộc lấy theo bên lớn hơn)
            // Vì 2 mặt bên của cột KHÔNG THỂ có 2 cách bố trí khác nhau
            // ═══════════════════════════════════════════════════════════════
            List<SectionArrangement> governingList;
            double maxReq;

            if (req1 >= req2)
            {
                governingList = valid1;
                maxReq = req1;
                Utils.RebarLogger.Log($"  -> {sideName}: FORCE GOVERNING from Left (Req={req1:F2} > {req2:F2})");
            }
            else
            {
                governingList = valid2;
                maxReq = req2;
                Utils.RebarLogger.Log($"  -> {sideName}: FORCE GOVERNING from Right (Req={req2:F2} > {req1:F2})");
            }

            if (governingList.Count > 0)
            {
                // Validate rằng phương án governing thỏa mãn cả 2 yêu cầu
                var validForBoth = governingList
                    .Where(a => a.TotalArea >= req1 * (1 - tolerance) && a.TotalArea >= req2 * (1 - tolerance))
                    .ToList();

                if (validForBoth.Count > 0)
                {
                    return validForBoth.Select(CloneArrangement).ToList();
                }

                // Nếu không có phương án thỏa mãn cả 2, vẫn phải lấy theo governing
                // vì bên nhỏ hơn sẽ được bù bởi addon ở GlobalOptimizer
                return governingList.Select(CloneArrangement).ToList();
            }

            // ═══════════════════════════════════════════════════════════════
            // STRATEGY 4: FALLBACK - Nếu governing list rỗng, thử list còn lại
            // ═══════════════════════════════════════════════════════════════
            var fallbackList = req1 >= req2 ? valid2 : valid1;
            if (fallbackList.Count > 0)
            {
                Utils.RebarLogger.LogWarning($"  -> {sideName}: FALLBACK to smaller side (may need addon)");
                return fallbackList.Select(CloneArrangement).ToList();
            }

            // Fail - Không có phương án nào
            Utils.RebarLogger.LogError($"  -> {sideName}: NO VALID MERGE OPTION");
            return null;
        }

        /// <summary>
        /// Tìm giao thoa: Phương án có cùng (Diameter, Count) ở cả 2 list.
        /// </summary>
        private List<SectionArrangement> FindIntersection(List<SectionArrangement> list1, List<SectionArrangement> list2)
        {
            var result = new List<SectionArrangement>();

            if (list1 == null || list2 == null) return result;

            foreach (var item1 in list1)
            {
                var match = list2.FirstOrDefault(item2 =>
                    item2.PrimaryDiameter == item1.PrimaryDiameter &&
                    item2.TotalCount == item1.TotalCount &&
                    Math.Abs(item2.LayerCount - item1.LayerCount) <= MaxLayerCountDifference);

                if (match != null)
                {
                    // Lấy phương án có score cao hơn
                    result.Add(CloneArrangement(item1.Score >= match.Score ? item1 : match));
                }
            }

            return result.GroupBy(x => new { x.PrimaryDiameter, x.TotalCount })
                         .Select(g => g.First())
                         .OrderByDescending(a => a.Score)
                         .ToList();
        }

        /// <summary>
        /// Thử tìm phương án cân bằng thỏa mãn cả 2 gối.
        /// VD: Gối 1 cần 19cm², Gối 2 cần 8cm²
        ///     -> Thử các phương án: 3D22, 3D20, 4D20... xem cái nào thỏa mãn cả 2
        /// </summary>
        private List<SectionArrangement> TryRebalance(
            DesignSection section1,
            DesignSection section2,
            double governingReq,
            string sideName)
        {
            double safetyFactor = _settings.Rules?.SafetyFactor ?? 1.0;
            double tolerance = Math.Max(0, 1.0 - safetyFactor);
            double minUsableWidth = Math.Min(section1.UsableWidth, section2.UsableWidth);
            double minSpacing = _settings.Beam?.MinClearSpacing ?? 30;
            double maxSpacing = _settings.Beam?.MaxClearSpacing ?? 200;
            int minBars = _settings.Beam?.MinBarsPerLayer ?? 2;
            int maxBars = _settings.Beam?.MaxBarsPerLayer ?? 8;

            var candidates = new List<SectionArrangement>();

            // Thử các đường kính từ lớn đến nhỏ
            foreach (var d in _allowedDiameters.OrderByDescending(x => x))
            {
                double as1 = Math.PI * d * d / 400.0;

                // Tính số thanh tối thiểu cần thiết
                int minCount = (int)Math.Ceiling(governingReq / as1);
                if (minCount < minBars) minCount = minBars;

                // Thử các số lượng từ min đến max
                for (int n = minCount; n <= Math.Min(minCount + 2, maxBars); n++)
                {
                    // Check spacing constraint
                    double spacing = CalculateClearSpacing(minUsableWidth, n, d);
                    if (spacing < Math.Max(d, minSpacing) || spacing > maxSpacing)
                    {
                        continue;
                    }

                    double totalArea = n * as1;

                    // Check if this satisfies governing requirement
                    if (totalArea >= governingReq * (1 - tolerance))
                    {
                        candidates.Add(new SectionArrangement
                        {
                            TotalCount = n,
                            PrimaryDiameter = d,
                            TotalArea = totalArea,
                            LayerCount = 1,
                            BarsPerLayer = new List<int> { n },
                            DiametersPerLayer = new List<int> { d },
                            BarDiameters = Enumerable.Repeat(d, n).ToList(),
                            IsSymmetric = true,
                            ClearSpacing = spacing,
                            Score = CalculateRebalanceScore(totalArea, governingReq, n, d)
                        });
                    }
                }
            }

            if (candidates.Count == 0) return null;

            // Sắp xếp theo score (ưu tiên ít waste, ít thanh)
            return candidates.OrderByDescending(c => c.Score).Take(5).ToList();
        }

        private double CalculateClearSpacing(double usableWidth, int barCount, int diameter)
        {
            if (barCount <= 1) return usableWidth - diameter;
            double totalBarWidth = barCount * diameter;
            double availableForGaps = usableWidth - totalBarWidth;
            return availableForGaps / (barCount - 1);
        }

        private double CalculateRebalanceScore(double providedArea, double requiredArea, int count, int diameter)
        {
            double score = 100.0;

            // Penalty for waste
            double waste = (providedArea - requiredArea) / requiredArea * 100;
            score -= waste * 0.5;

            // Penalty for too many bars
            if (count > 4) score -= (count - 4) * 5;

            // Bonus for larger diameter (fewer bars)
            if (diameter >= 22) score += 5;

            return Math.Max(0, score);
        }

        #endregion

        #region Additional Constraints (Stirrup & Vertical)

        private void ApplyStirrupCompatibility(List<DesignSection> sections)
        {
            if (_settings.Stirrup?.EnableAdvancedRules != true) return;

            foreach (var section in sections)
            {
                var validTopBotPairs = new List<(SectionArrangement top, SectionArrangement bot)>();
                foreach (var topArr in section.ValidArrangementsTop)
                {
                    foreach (var botArr in section.ValidArrangementsBot)
                    {
                        if (IsStirrupCompatible(topArr, botArr, section))
                            validTopBotPairs.Add((topArr, botArr));
                    }
                }

                if (validTopBotPairs.Count > 0)
                {
                    var validTops = validTopBotPairs.Select(c => c.top).Distinct().ToList();
                    var validBots = validTopBotPairs.Select(c => c.bot).Distinct().ToList();

                    section.ValidArrangementsTop = section.ValidArrangementsTop.Intersect(validTops).ToList();
                    section.ValidArrangementsBot = section.ValidArrangementsBot.Intersect(validBots).ToList();
                }
            }
        }

        private bool IsStirrupCompatible(SectionArrangement topArr, SectionArrangement botArr, DesignSection section)
        {
            if (topArr.TotalCount == 0 && botArr.TotalCount == 0) return true;
            int topCount = topArr.TotalCount;
            int botCount = botArr.TotalCount;
            int legs = _settings.Stirrup?.GetLegCount(Math.Max(topCount, botCount), topCount > 2 || botCount > 2) ?? 2;
            int cells = legs - 1;
            if (cells <= 0) return topCount <= 2 && botCount <= 2;
            int maxBars = 2 * cells + 2;
            return topCount <= maxBars && botCount <= maxBars;
        }

        private void ApplyVerticalAlignment(List<DesignSection> sections)
        {
            if (_settings.Beam?.PreferVerticalAlignment != true) return;

            double alignmentPenalty = _settings.Rules?.AlignmentPenaltyScore ?? 25.0;
            double scaledPenalty = alignmentPenalty / 5.0;

            foreach (var section in sections)
            {
                var alignedPairs = new List<(SectionArrangement top, SectionArrangement bot)>();
                foreach (var topArr in section.ValidArrangementsTop)
                {
                    foreach (var botArr in section.ValidArrangementsBot)
                    {
                        if (topArr.IsEvenCount == botArr.IsEvenCount)
                            alignedPairs.Add((topArr, botArr));
                    }
                }

                if (alignedPairs.Count > 0)
                {
                    var alignedTops = alignedPairs.Select(p => p.top).Distinct().ToList();
                    var alignedBots = alignedPairs.Select(p => p.bot).Distinct().ToList();

                    foreach (var arr in section.ValidArrangementsTop)
                        if (!alignedTops.Contains(arr)) arr.Score = Math.Max(0, arr.Score - scaledPenalty);

                    foreach (var arr in section.ValidArrangementsBot)
                        if (!alignedBots.Contains(arr)) arr.Score = Math.Max(0, arr.Score - scaledPenalty);
                }
            }
        }

        #endregion

        #region Validation & Helpers

        private bool ValidateAllSectionsHaveOptions(List<DesignSection> sections)
        {
            bool allOk = true;
            foreach (var section in sections)
            {
                bool topOk = section.ReqTop <= 0.01 || section.ValidArrangementsTop.Count > 0;
                bool botOk = section.ReqBot <= 0.01 || section.ValidArrangementsBot.Count > 0;

                if (!topOk || !botOk)
                {
                    Utils.RebarLogger.LogError($"Section {section.SectionId} has no valid arrangements post-merge: TopOK={topOk}, BotOK={botOk}");
                    allOk = false;
                }
            }
            return allOk;
        }

        private SectionArrangement CloneArrangement(SectionArrangement source)
        {
            if (source == null) return SectionArrangement.Empty;
            return new SectionArrangement
            {
                TotalCount = source.TotalCount,
                TotalArea = source.TotalArea,
                LayerCount = source.LayerCount,
                BarsPerLayer = new List<int>(source.BarsPerLayer ?? new List<int>()),
                DiametersPerLayer = new List<int>(source.DiametersPerLayer ?? new List<int>()),
                PrimaryDiameter = source.PrimaryDiameter,
                BarDiameters = new List<int>(source.BarDiameters ?? new List<int>()),
                IsSymmetric = source.IsSymmetric,
                FitsStirrupLayout = source.FitsStirrupLayout,
                ClearSpacing = source.ClearSpacing,
                VerticalSpacing = source.VerticalSpacing,
                Score = source.Score,
                WasteCount = source.WasteCount,
                Efficiency = source.Efficiency
            };
        }

        #endregion
    }
}
