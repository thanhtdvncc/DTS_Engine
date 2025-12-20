using System;
using System.Collections.Generic;
using System.Linq;
using DTS_Engine.Core.Algorithms.Rebar.Models;
using DTS_Engine.Core.Data;

namespace DTS_Engine.Core.Algorithms.Rebar.Pipeline.Stages
{
    /// <summary>
    /// Stage 4: Kiểm tra và báo cáo xung đột thiết kế.
    /// - Stirrup không đủ ôm thép dọc
    /// - Khoảng hở thực tế không đủ
    /// - Layer inconsistency giữa các nhịp
    /// Không reject phương án, chỉ generate warning/conflicts.
    /// </summary>
    public class ConflictResolver : IRebarPipelineStage
    {
        public string StageName { get { return "ConflictResolver"; } }
        public int Order { get { return 4; } }

        public IEnumerable<SolutionContext> Execute(
            IEnumerable<SolutionContext> inputs,
            ProjectConstraints globalConstraints)
        {
            foreach (var ctx in inputs)
            {
                if (!ctx.IsValid)
                {
                    yield return ctx;
                    continue;
                }

                // Run conflict checks
                CheckStirrupLegConflicts(ctx);
                CheckClearSpacingConflicts(ctx);
                CheckLayerInconsistency(ctx);
                CheckDiameterJumps(ctx);

                yield return ctx;
            }
        }

        /// <summary>
        /// Check 1: Số nhánh đai có đủ ôm tất cả thép lớp 1 không?
        /// Rule: Mỗi thanh thép lớp 1 phải nằm trong góc của đai
        /// Stirrup legs >= Layer1 bars (both Top and Bot)
        /// </summary>
        private void CheckStirrupLegConflicts(SolutionContext ctx)
        {
            var sol = ctx.CurrentSolution;
            if (sol == null) return;

            int stirrupLegs = ctx.StirrupLegCount;
            if (stirrupLegs <= 0) stirrupLegs = 2; // Minimum

            // Get max bars in Layer 1 from reinforcements
            int maxLayer1Top = sol.BackboneCount_Top;
            int maxLayer1Bot = sol.BackboneCount_Bot;

            foreach (var kvp in sol.Reinforcements)
            {
                var spec = kvp.Value;
                if (spec.LayerBreakdown != null && spec.LayerBreakdown.Count > 0)
                {
                    int layer1Count = spec.LayerBreakdown[0];
                    if (kvp.Key.Contains("Top"))
                        maxLayer1Top = Math.Max(maxLayer1Top, layer1Count);
                    else
                        maxLayer1Bot = Math.Max(maxLayer1Bot, layer1Count);
                }
            }

            int maxLayer1 = Math.Max(maxLayer1Top, maxLayer1Bot);

            // Standard check: stirrup legs should match or exceed layer 1 bars
            // For 2-leg stirrup: max 2 bars at corners
            // For 4-leg stirrup: max 4 bars (2 corners + 2 intermediate)
            if (maxLayer1 > stirrupLegs)
            {
                ctx.Conflicts.Add(new ConflictReport
                {
                    ConflictType = "StirrupLegDeficit",
                    SpanId = "All",
                    Description = string.Format(
                        "Số nhánh đai ({0}) ít hơn số thanh lớp 1 ({1}). Một số thanh sẽ không được đai ôm trực tiếp.",
                        stirrupLegs, maxLayer1),
                    SuggestedFix = string.Format(
                        "Tăng số nhánh đai lên {0} hoặc dùng đai kín + đai móc.",
                        maxLayer1)
                });
            }
        }

        /// <summary>
        /// Check 2: Khoảng hở thực tế có đủ cho cốt liệu lọt qua không?
        /// </summary>
        private void CheckClearSpacingConflicts(SolutionContext ctx)
        {
            var sol = ctx.CurrentSolution;
            var settings = ctx.Settings;
            if (sol == null || settings == null) return;

            double cover = settings.Beam?.CoverSide ?? 25;
            double stirrup = settings.Beam?.EstimatedStirrupDiameter ?? 10;
            int aggregateSize = settings.Beam?.AggregateSize ?? 20;
            double minClearSpacing = Math.Max(
                settings.Beam?.MinClearSpacing ?? 25,
                1.33 * aggregateSize // TCVN requirement
            );

            double usableWidth = ctx.BeamWidth - 2 * cover - 2 * stirrup;
            if (usableWidth <= 0) return;

            // Calculate actual clear spacing for Layer 1
            int maxBarsLayer1 = Math.Max(sol.BackboneCount_Top, sol.BackboneCount_Bot);
            foreach (var kvp in sol.Reinforcements)
            {
                var spec = kvp.Value;
                if (spec.LayerBreakdown != null && spec.LayerBreakdown.Count > 0)
                {
                    maxBarsLayer1 = Math.Max(maxBarsLayer1, spec.LayerBreakdown[0]);
                }
            }

            if (maxBarsLayer1 <= 1) return;

            int dia = sol.BackboneDiameter;
            // Actual spacing: (usable - n*d) / (n-1)
            double totalBarWidth = maxBarsLayer1 * dia;
            double actualClearSpacing = (usableWidth - totalBarWidth) / (maxBarsLayer1 - 1);

            if (actualClearSpacing < minClearSpacing)
            {
                ctx.Conflicts.Add(new ConflictReport
                {
                    ConflictType = "InsufficientClearSpacing",
                    SpanId = "All",
                    Description = string.Format(
                        "Khoảng hở tịnh thực tế ({0:F0}mm) < yêu cầu ({1:F0}mm). Cốt liệu có thể không lọt qua.",
                        actualClearSpacing, minClearSpacing),
                    SuggestedFix = "Giảm số thanh lớp 1 hoặc tăng bề rộng dầm, hoặc dùng đường kính nhỏ hơn."
                });
            }
        }

        /// <summary>
        /// Check 3: Layer inconsistency giữa các nhịp liên tiếp.
        /// VD: Nhịp 1 có 1 lớp thép, Nhịp 2 có 2 lớp → khó uốn thép liên tục.
        /// </summary>
        private void CheckLayerInconsistency(SolutionContext ctx)
        {
            var sol = ctx.CurrentSolution;
            var group = ctx.Group;
            if (sol == null || group?.Spans == null) return;

            // Track layer counts per span
            var spanLayers = new Dictionary<string, int>();

            foreach (var kvp in sol.Reinforcements)
            {
                var spec = kvp.Value;
                // Extract span ID from key (e.g., "S1_Top_Left" → "S1")
                string spanId = kvp.Key.Split('_')[0];

                int layers = spec.LayerBreakdown?.Count ?? 1;
                if (!spanLayers.ContainsKey(spanId) || spanLayers[spanId] < layers)
                    spanLayers[spanId] = layers;
            }

            // Check adjacent spans for layer jumps
            var spanIds = group.Spans.Select(s => s.SpanId).ToList();
            for (int i = 0; i < spanIds.Count - 1; i++)
            {
                string span1 = spanIds[i];
                string span2 = spanIds[i + 1];

                int layers1 = spanLayers.ContainsKey(span1) ? spanLayers[span1] : 1;
                int layers2 = spanLayers.ContainsKey(span2) ? spanLayers[span2] : 1;

                if (Math.Abs(layers1 - layers2) > 1)
                {
                    ctx.Conflicts.Add(new ConflictReport
                    {
                        ConflictType = "LayerJump",
                        SpanId = string.Format("{0}-{1}", span1, span2),
                        Description = string.Format(
                            "Nhịp {0} có {1} lớp, nhịp {2} có {3} lớp. Khó uốn thép liên tục.",
                            span1, layers1, span2, layers2),
                        SuggestedFix = "Cân nhắc đồng bộ số lớp giữa các nhịp liền kề."
                    });
                }
            }
        }

        /// <summary>
        /// Check 4: Diameter jumps giữa thép chủ và thép gia cường.
        /// VD: Backbone D16, Add bars D25 → tỷ lệ > 1.5 gây khó đầm.
        /// </summary>
        private void CheckDiameterJumps(SolutionContext ctx)
        {
            var sol = ctx.CurrentSolution;
            if (sol == null) return;

            int backboneDia = sol.BackboneDiameter;
            if (backboneDia <= 0) return;

            foreach (var kvp in sol.Reinforcements)
            {
                var spec = kvp.Value;
                if (spec.Count <= 0 || spec.Diameter <= 0) continue;

                double ratio = (double)spec.Diameter / backboneDia;

                // Warning if diameter jump > 1.5x (e.g., D16 → D25)
                if (ratio > 1.5)
                {
                    ctx.Conflicts.Add(new ConflictReport
                    {
                        ConflictType = "DiameterJump",
                        SpanId = kvp.Key,
                        Description = string.Format(
                            "Thép gia cường D{0} lớn hơn 1.5x thép chủ D{1} (tỷ lệ {2:F2}).",
                            spec.Diameter, backboneDia, ratio),
                        SuggestedFix = "Cân nhắc dùng đường kính gần hơn để dễ đầm bê tông."
                    });
                    break; // Only report once per solution
                }
            }
        }
    }
}
