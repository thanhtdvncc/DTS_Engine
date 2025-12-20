using System;
using System.Collections.Generic;
using DTS_Engine.Core.Algorithms.Rebar.Models;

namespace DTS_Engine.Core.Algorithms.Rebar.Strategies
{
    /// <summary>
    /// Chiến thuật BALANCED: Cố gắng chia đều thép qua các lớp.
    /// 
    /// DYNAMIC N-LAYER: Hỗ trợ tối đa MaxLayers lớp (không còn hardcode 2 lớp).
    /// 
    /// Ưu điểm: Phân bố đều, trọng tâm mặt cắt tốt hơn cho mô men.
    ///          Có thể tránh được trường hợp 3+1 waste.
    /// Nhược điểm: Phức tạp hơn để thi công.
    /// </summary>
    public class BalancedFillingStrategy : IFillingStrategy
    {
        public string StrategyName { get { return "Balanced"; } }

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
            // BALANCED STRATEGY: Try to distribute evenly
            // Key insight: For 2 layers, prefer 2+2 over 3+1
            // For 3 layers: prefer 3+2+1 or 2+2+2 over 4+1+1
            // ═══════════════════════════════════════════════════════════════

            // First, determine how many layers we actually need
            int layersNeeded = DetermineLayersNeeded(totalNeeded, capacity, maxLayers);

            if (layersNeeded > maxLayers)
            {
                return new FillingResult
                {
                    IsValid = false,
                    FailReason = $"Cần {layersNeeded} lớp nhưng max={maxLayers}"
                };
            }

            // Distribute bars as evenly as possible
            var layerCounts = DistributeBalanced(totalNeeded, layersNeeded, capacity, backboneCount);

            if (layerCounts == null)
            {
                return new FillingResult
                {
                    IsValid = false,
                    FailReason = $"Không thể phân bố {totalNeeded} thanh qua {layersNeeded} lớp"
                };
            }

            // ═══════════════════════════════════════════════════════════════
            // APPLY CONSTRAINTS: Symmetry, MinBarsPerLayer, etc.
            // V3.3: Get minBarsPerLayer from settings
            // ═══════════════════════════════════════════════════════════════
            int wasteCount = 0;
            int minBarsPerLayer = settings?.Beam?.MinBarsPerLayer ?? 2;
            return ApplyConstraints(layerCounts, capacity, backboneCount, legCount, preferSymmetric, minBarsPerLayer, ref wasteCount);
        }

        /// <summary>
        /// Determine minimum layers needed for totalBars
        /// </summary>
        private int DetermineLayersNeeded(int totalBars, int capacity, int maxLayers)
        {
            int remaining = totalBars;
            int prevLayerMax = capacity;

            for (int layer = 1; layer <= maxLayers; layer++)
            {
                remaining -= prevLayerMax;
                if (remaining <= 0) return layer;
                prevLayerMax = Math.Min(prevLayerMax, capacity); // Pyramid constraint
            }

            return maxLayers + 1; // Indicates not possible
        }

        /// <summary>
        /// Distribute bars as evenly as possible across layers, respecting Pyramid Rule
        /// </summary>
        private List<int> DistributeBalanced(int totalBars, int numLayers, int capacity, int backboneCount)
        {
            if (numLayers <= 0) return null;

            var layerCounts = new List<int>(new int[numLayers]);

            // Start with even distribution
            int basePerLayer = totalBars / numLayers;
            int remainder = totalBars % numLayers;

            // Fill from top (layer 0) to bottom, adding remainder to top layers
            for (int i = 0; i < numLayers; i++)
            {
                layerCounts[i] = basePerLayer + (i < remainder ? 1 : 0);
            }

            // Ensure Layer 1 >= backbone
            if (layerCounts[0] < backboneCount)
            {
                int deficit = backboneCount - layerCounts[0];
                layerCounts[0] = backboneCount;

                // Redistribute deficit from other layers
                for (int i = numLayers - 1; i >= 1 && deficit > 0; i--)
                {
                    int canTake = Math.Min(deficit, layerCounts[i]);
                    layerCounts[i] -= canTake;
                    deficit -= canTake;
                }
            }

            // Ensure Pyramid Rule: Sort descending (re-balance if needed)
            layerCounts.Sort((a, b) => b.CompareTo(a));

            // Ensure L1 <= capacity
            if (layerCounts[0] > capacity)
            {
                int overflow = layerCounts[0] - capacity;
                layerCounts[0] = capacity;

                // Push overflow to next layers
                for (int i = 1; i < numLayers && overflow > 0; i++)
                {
                    int prevLayer = layerCounts[i - 1];
                    int canAdd = prevLayer - layerCounts[i]; // Maintain pyramid
                    int toAdd = Math.Min(overflow, canAdd);
                    layerCounts[i] += toAdd;
                    overflow -= toAdd;
                }

                if (overflow > 0) return null; // Cannot fit
            }

            // Final validation
            int sum = 0;
            foreach (int c in layerCounts) sum += c;
            if (sum < totalBars) return null;

            // Remove trailing zero layers
            while (layerCounts.Count > 1 && layerCounts[layerCounts.Count - 1] == 0)
            {
                layerCounts.RemoveAt(layerCounts.Count - 1);
            }

            return layerCounts;
        }

        private FillingResult ApplyConstraints(
            List<int> layerCounts, int capacity, int backboneCount, int legCount,
            bool preferSymmetric, int minBarsPerLayer, ref int wasteCount)
        {
            // CONSTRAINT 1: Pyramid Rule validation
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

            // CONSTRAINT 3: Snap-to-Structure (Stirrup Legs)
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
