using System;
using System.Collections.Generic;
using DTS_Engine.Core.Algorithms.Rebar.Models;

namespace DTS_Engine.Core.Algorithms.Rebar.Strategies
{
    /// <summary>
    /// Chiến thuật GREEDY: Ưu tiên nhồi lớp 1 trước, chỉ tràn sang lớp tiếp theo khi cần.
    /// 
    /// DYNAMIC N-LAYER: Hỗ trợ tối đa MaxLayers lớp (không còn hardcode 2 lớp).
    /// Pyramid Rule: L[n] <= L[n-1] cho mọi n.
    /// 
    /// Ưu điểm: Tập trung thép, dễ đổ bê tông.
    /// Nhược điểm: Có thể cần nhiều thanh hơn nếu lớp 1 đầy.
    /// </summary>
    public class GreedyFillingStrategy : IFillingStrategy
    {
        public string StrategyName { get { return "Greedy"; } }

        public FillingResult Calculate(FillingContext context)
        {
            var settings = context.Settings;
            int maxLayers = context.MaxLayers;
            bool preferSymmetric = settings?.Beam?.PreferSymmetric ?? true;

            int capacity = context.LayerCapacity;
            int backboneCount = context.BackboneCount;
            int legCount = context.StirrupLegCount;

            // Calculate missing area (what backbone doesn't cover)
            double missing = context.RequiredArea - context.BackboneArea;

            // ═══════════════════════════════════════════════════════════════
            // CASE 1: Backbone đã đủ → chỉ trả về L1 = backbone
            // ═══════════════════════════════════════════════════════════════
            if (missing <= 0.01)
            {
                return new FillingResult
                {
                    IsValid = true,
                    LayerCounts = new List<int> { backboneCount }
                };
            }

            // Calculate total bars needed (including backbone)
            double barArea = Math.PI * context.BackboneDiameter * context.BackboneDiameter / 400.0;
            int totalNeeded = (int)Math.Ceiling(context.RequiredArea / barArea);

            // ═══════════════════════════════════════════════════════════════
            // DYNAMIC N-LAYER GREEDY FILLING
            // ═══════════════════════════════════════════════════════════════
            var layerCounts = new List<int>();
            int remaining = totalNeeded;
            int prevLayerMax = capacity;  // First layer can fill up to capacity
            int wasteCount = 0;

            for (int layer = 0; layer < maxLayers && remaining > 0; layer++)
            {
                int thisLayerMax;
                if (layer == 0)
                {
                    // Layer 1: Fill to capacity but must include backbone
                    thisLayerMax = capacity;
                }
                else
                {
                    // Layer 2+: Pyramid rule - cannot exceed previous layer
                    thisLayerMax = layerCounts[layer - 1];
                }

                int barsThisLayer = Math.Min(remaining, thisLayerMax);

                // Layer 1 must include backbone
                if (layer == 0)
                {
                    barsThisLayer = Math.Max(barsThisLayer, backboneCount);
                }

                layerCounts.Add(barsThisLayer);
                remaining -= barsThisLayer;
            }

            // ═══════════════════════════════════════════════════════════════
            // FAIL: Không đủ chỗ với maxLayers lớp
            // ═══════════════════════════════════════════════════════════════
            if (remaining > 0)
            {
                return new FillingResult
                {
                    IsValid = false,
                    FailReason = $"Không thể bố trí {totalNeeded} thanh với {maxLayers} lớp (capacity={capacity})"
                };
            }

            // ═══════════════════════════════════════════════════════════════
            // APPLY CONSTRAINTS: Symmetry, MinBarsPerLayer, etc.
            // V3.3: Get minBarsPerLayer from settings
            // ═══════════════════════════════════════════════════════════════
            int minBarsPerLayer = settings?.Beam?.MinBarsPerLayer ?? 2;
            return ApplyConstraints(layerCounts, capacity, backboneCount, legCount, preferSymmetric, minBarsPerLayer, ref wasteCount);
        }

        private FillingResult ApplyConstraints(
            List<int> layerCounts, int capacity, int backboneCount, int legCount,
            bool preferSymmetric, int minBarsPerLayer, ref int wasteCount)
        {
            // CONSTRAINT 1: Pyramid Rule (L[n] <= L[n-1]) - already ensured by greedy loop
            for (int i = 1; i < layerCounts.Count; i++)
            {
                if (layerCounts[i] > layerCounts[i - 1])
                {
                    return new FillingResult
                    {
                        IsValid = false,
                        FailReason = $"Vi phạm Pyramid Rule: L{i + 1}={layerCounts[i]} > L{i}={layerCounts[i - 1]}"
                    };
                }
            }

            // CONSTRAINT 2: Capacity check for L1
            if (layerCounts.Count > 0 && layerCounts[0] > capacity)
            {
                return new FillingResult
                {
                    IsValid = false,
                    FailReason = $"L1={layerCounts[0]} vượt capacity={capacity}"
                };
            }

            // CONSTRAINT 3: Snap-to-Structure (Stirrup Legs) cho các lớp có thép
            if (legCount > 2)
            {
                for (int i = 1; i < layerCounts.Count; i++)
                {
                    int n = layerCounts[i];
                    int prevLayer = layerCounts[i - 1];
                    if (n > 0 && n >= legCount - 1 && n < legCount && n <= prevLayer)
                    {
                        layerCounts[i] = legCount;
                    }
                }
            }

            // CONSTRAINT 4: Symmetry - prefer even counts
            if (preferSymmetric)
            {
                for (int i = 0; i < layerCounts.Count; i++)
                {
                    int n = layerCounts[i];
                    int maxForThisLayer = (i == 0) ? capacity : layerCounts[i - 1];
                    if (n % 2 != 0 && n + 1 <= maxForThisLayer)
                    {
                        layerCounts[i] = n + 1;
                    }
                }
            }

            // CONSTRAINT 5: MinBarsPerLayer - layers 2+ must have at least min bars if any
            // V3.3: minBarsPerLayer passed as parameter from settings
            for (int i = 1; i < layerCounts.Count; i++)
            {
                int n = layerCounts[i];
                int prevLayer = layerCounts[i - 1];
                if (n > 0 && n < minBarsPerLayer)
                {
                    if (minBarsPerLayer <= prevLayer)
                    {
                        wasteCount += minBarsPerLayer - n;
                        layerCounts[i] = minBarsPerLayer;
                    }
                    else
                    {
                        return new FillingResult
                        {
                            IsValid = false,
                            FailReason = $"L{i + 1} chỉ có {n} thanh, cần tối thiểu {minBarsPerLayer}"
                        };
                    }
                }
            }

            // Re-validate Pyramid after adjustments
            for (int i = 1; i < layerCounts.Count; i++)
            {
                if (layerCounts[i] > layerCounts[i - 1])
                {
                    return new FillingResult
                    {
                        IsValid = false,
                        FailReason = "Không thể thỏa mãn ràng buộc sau khi điều chỉnh symmetry"
                    };
                }
            }

            return new FillingResult
            {
                IsValid = true,
                LayerCounts = layerCounts,
                WasteCount = wasteCount
            };
        }
    }
}
