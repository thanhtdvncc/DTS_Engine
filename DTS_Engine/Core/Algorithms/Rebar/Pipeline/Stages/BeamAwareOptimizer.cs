using System;
using System.Collections.Generic;
using System.Linq;
using DTS_Engine.Core.Algorithms.Rebar.Models;
using DTS_Engine.Core.Algorithms.Rebar.Utils;
using DTS_Engine.Core.Data;
using DTS_Engine.Core.Utils;

namespace DTS_Engine.Core.Algorithms.Rebar.Pipeline.Stages
{
    /// <summary>
    /// V4.0: Thuật toán "Xương Sống & Thịt Đắp" (Backbone & Addon)
    /// Sử dụng Branch & Bound với Local Greedy cho Addon.
    /// 
    /// PHASE 0: Pre-calculation (Lọc đường kính vô lý)
    /// PHASE 1: Backbone Selection (Branch & Bound - Chọn thép chủ)
    /// PHASE 2: Local Addon Filling (Greedy per-position - Đắp gia cường)
    /// </summary>
    public class BeamAwareOptimizer
    {
        #region Data Structures

        /// <summary>
        /// Thông tin ràng buộc của một đường kính tại một vị trí
        /// </summary>
        public class DiameterConstraint
        {
            public int Diameter { get; set; }
            public double AreaPerBar { get; set; }
            public int CapacityPerLayer { get; set; }   // N_capacity: số thanh max/lớp
            public int AbsoluteMax { get; set; }        // N_capacity × MaxLayers
            public int MinRequired { get; set; }        // Ceil(As_req / As_bar)
            public bool IsValid { get; set; }           // MinRequired <= AbsoluteMax?
        }

        /// <summary>
        /// Thông tin yêu cầu tại một vị trí (gối/nhịp)
        /// </summary>
        public class PositionRequirement
        {
            public string PositionId { get; set; }      // "Support0", "MidSpan1", etc.
            public bool IsTop { get; set; }
            public double Width { get; set; }           // mm
            public double AsRequired { get; set; }      // cm²
            public List<DiameterConstraint> ValidDiameters { get; set; } = new List<DiameterConstraint>();
        }

        /// <summary>
        /// Cấu hình backbone
        /// </summary>
        public class BackboneConfig
        {
            public int TopDiameter { get; set; }
            public int TopCount { get; set; }
            public int BotDiameter { get; set; }
            public int BotCount { get; set; }

            public double TopArea => TopCount * Math.PI * TopDiameter * TopDiameter / 400.0;
            public double BotArea => BotCount * Math.PI * BotDiameter * BotDiameter / 400.0;

            public double BaseWeight(double lengthMM, double density = 7850)
            {
                // Weight = Volume × Density
                double topVolume = TopCount * Math.PI * Math.Pow(TopDiameter / 2.0, 2) * lengthMM / 1e9; // m³
                double botVolume = BotCount * Math.PI * Math.Pow(BotDiameter / 2.0, 2) * lengthMM / 1e9;
                return (topVolume + botVolume) * density; // kg
            }

            public string ToId() => $"T:{TopCount}D{TopDiameter}/B:{BotCount}D{BotDiameter}";
        }

        /// <summary>
        /// Cấu hình addon tại một vị trí
        /// </summary>
        public class AddonConfig
        {
            public string PositionId { get; set; }
            public bool IsTop { get; set; }
            public int Diameter { get; set; }
            public int Count { get; set; }
            public int Layer { get; set; }              // 1, 2, or 3

            public double Area => Count * Math.PI * Diameter * Diameter / 400.0;

            public static AddonConfig None(string posId, bool isTop) => new AddonConfig
            {
                PositionId = posId,
                IsTop = isTop,
                Diameter = 0,
                Count = 0,
                Layer = 0
            };
        }

        #endregion

        #region Main Optimization Entry

        /// <summary>
        /// Tối ưu hóa giải pháp cho một BeamGroup.
        /// Trả về danh sách các Solution tốt nhất (sorted by weight).
        /// </summary>
        public List<ContinuousBeamSolution> Optimize(
            BeamGroup group,
            List<BeamResultData> spanResults,
            DtsSettings settings)
        {
            RebarLogger.LogPhase("V4.0 BEAM-AWARE OPTIMIZER");

            // ═══════════════════════════════════════════════════════════════
            // PHASE 0: PRE-CALCULATION
            // ═══════════════════════════════════════════════════════════════
            var positions = ExtractAllPositions(group, spanResults, settings);
            RebarLogger.Log($"PHASE 0: Extracted {positions.Count} positions");

            var allowedDiameters = DiameterParser.ParseRange(
                settings.Beam?.MainBarRange ?? "16-25",
                settings.General?.AvailableDiameters ?? new List<int> { 16, 18, 20, 22, 25 }
            );

            // Pre-calc constraints for each position × diameter
            foreach (var pos in positions)
            {
                pos.ValidDiameters = CalculateDiameterConstraints(pos, allowedDiameters, settings);
                RebarLogger.Log($"  {pos.PositionId}: AsReq={pos.AsRequired:F2}cm², ValidDias={pos.ValidDiameters.Count}");
            }

            // Filter diameters that are impossible everywhere
            var globalValidDias = FilterGloballyValidDiameters(positions, allowedDiameters);
            RebarLogger.Log($"PHASE 0: Globally valid diameters = [{string.Join(",", globalValidDias)}]");

            if (globalValidDias.Count == 0)
            {
                RebarLogger.LogError("No valid diameters found for any position!");
                return new List<ContinuousBeamSolution>();
            }

            // ═══════════════════════════════════════════════════════════════
            // PHASE 1: BACKBONE SELECTION (Branch & Bound)
            // ═══════════════════════════════════════════════════════════════
            RebarLogger.LogPhase("PHASE 1: BACKBONE SELECTION");

            var solutions = new List<ContinuousBeamSolution>();
            double bestWeight = double.MaxValue;
            double totalLength = group.Spans?.Sum(s => s.Length) ?? 6000;

            // Get capacity for smallest diameter (max bars)
            double minWidth = group.Spans?.Min(s => s.Width) ?? 300;

            // Sort diameters: smaller first (lighter backbone)
            var sortedDias = globalValidDias.OrderBy(d => d).ToList();

            // V4.0.1: Calculate max As_required for Top and Bot (for early exit at 110%)
            double maxAsTopReq = positions.Where(p => p.IsTop).Select(p => p.AsRequired).DefaultIfEmpty(0).Max();
            double maxAsBotReq = positions.Where(p => !p.IsTop).Select(p => p.AsRequired).DefaultIfEmpty(0).Max();
            const double EXCESS_THRESHOLD = 1.10; // Exit if backbone > 110% of max required
            RebarLogger.Log($"Max As_required: Top={maxAsTopReq:F2}cm², Bot={maxAsBotReq:F2}cm² (Exit threshold: {EXCESS_THRESHOLD:P0})");

            int backboneCount = 0;
            int prunedCount = 0;
            int excessPrunedCount = 0;

            foreach (int topDia in sortedDias)
                foreach (int botDia in sortedDias)
                {
                    int topCapacity = CalculateCapacityPerLayer(minWidth, topDia, settings);
                    int botCapacity = CalculateCapacityPerLayer(minWidth, botDia, settings);

                    int topMinBars = Math.Max(2, settings.Beam?.MinBarsPerLayer ?? 2);
                    int botMinBars = Math.Max(2, settings.Beam?.MinBarsPerLayer ?? 2);

                    foreach (int nTop in Enumerable.Range(topMinBars, topCapacity - topMinBars + 1))
                        foreach (int nBot in Enumerable.Range(botMinBars, botCapacity - botMinBars + 1))
                        {
                            backboneCount++;
                            var backbone = new BackboneConfig
                            {
                                TopDiameter = topDia,
                                TopCount = nTop,
                                BotDiameter = botDia,
                                BotCount = nBot
                            };

                            // ═══ V4.0.1: EXCESS CHECK - Skip if backbone > 110% of required
                            // This prevents exploring excessively over-reinforced solutions
                            if (backbone.TopArea > maxAsTopReq * EXCESS_THRESHOLD &&
                                backbone.BotArea > maxAsBotReq * EXCESS_THRESHOLD)
                            {
                                excessPrunedCount++;
                                continue;
                            }

                            double baseWeight = backbone.BaseWeight(totalLength);

                            // ═══ BOUND CHECK: Skip if backbone alone > best solution
                            if (baseWeight >= bestWeight)
                            {
                                prunedCount++;
                                continue;
                            }

                            // ═══════════════════════════════════════════════════════
                            // PHASE 2: LOCAL ADDON FILLING
                            // ═══════════════════════════════════════════════════════
                            bool isFeasible = true;
                            double currentWeight = baseWeight;
                            var addons = new List<AddonConfig>();

                            foreach (var pos in positions)
                            {
                                double backboneArea = pos.IsTop ? backbone.TopArea : backbone.BotArea;
                                double deficit = pos.AsRequired - backboneArea;

                                if (deficit <= 0)
                                {
                                    addons.Add(AddonConfig.None(pos.PositionId, pos.IsTop));
                                    continue;
                                }

                                // Find best addon for this position (Greedy)
                                var bestAddon = FindBestAddonForPosition(
                                    pos, deficit, backbone, settings, globalValidDias
                                );

                                if (bestAddon == null)
                                {
                                    isFeasible = false;
                                    break;
                                }

                                addons.Add(bestAddon);
                                currentWeight += CalculateAddonWeight(bestAddon, pos.Width, settings);

                                // ═══ BOUND CHECK during addon filling
                                if (currentWeight >= bestWeight)
                                {
                                    isFeasible = false;
                                    break;
                                }
                            }

                            if (isFeasible)
                            {
                                bestWeight = currentWeight;
                                var solution = CreateSolution(backbone, addons, group, settings);
                                solution.TotalSteelWeight = currentWeight;
                                solutions.Add(solution);

                                RebarLogger.Log($"  ✅ {backbone.ToId()}: {currentWeight:F1}kg");
                            }
                        }
                }

            RebarLogger.Log($"PHASE 1 STATS: {backboneCount} tested, {prunedCount} weight-pruned, {excessPrunedCount} excess-pruned, {solutions.Count} valid");

            // Return top solutions sorted by weight
            return solutions
                .OrderBy(s => s.TotalSteelWeight)
                .Take(10)
                .ToList();
        }

        #endregion

        #region Phase 0: Pre-calculation

        private List<PositionRequirement> ExtractAllPositions(
            BeamGroup group,
            List<BeamResultData> spanResults,
            DtsSettings settings)
        {
            var positions = new List<PositionRequirement>();
            int numSpans = Math.Min(group.Spans?.Count ?? 0, spanResults?.Count ?? 0);
            double safetyFactor = 1.0; // Already applied in SAP results

            for (int i = 0; i < numSpans; i++)
            {
                var span = group.Spans[i];
                var result = spanResults[i];
                if (span == null || result == null) continue;

                double width = span.Width > 0 ? span.Width : 300;

                // Support Left (Top) - only for first span
                if (i == 0 && result.TopArea?.Length > 0)
                {
                    positions.Add(new PositionRequirement
                    {
                        PositionId = $"S{i + 1}_Top_L",
                        IsTop = true,
                        Width = width,
                        AsRequired = result.TopArea[0] * safetyFactor
                    });
                }

                // Support Right (Top)
                if (result.TopArea?.Length > 2)
                {
                    positions.Add(new PositionRequirement
                    {
                        PositionId = $"S{i + 1}_Top_R",
                        IsTop = true,
                        Width = width,
                        AsRequired = result.TopArea[2] * safetyFactor
                    });
                }

                // MidSpan (Bot)
                if (result.BotArea?.Length > 1)
                {
                    positions.Add(new PositionRequirement
                    {
                        PositionId = $"S{i + 1}_Bot_M",
                        IsTop = false,
                        Width = width,
                        AsRequired = result.BotArea[1] * safetyFactor
                    });
                }
            }

            return positions;
        }

        private List<DiameterConstraint> CalculateDiameterConstraints(
            PositionRequirement pos,
            List<int> diameters,
            DtsSettings settings)
        {
            var constraints = new List<DiameterConstraint>();
            int maxLayers = settings.Beam?.MaxLayers ?? 2;

            foreach (int dia in diameters)
            {
                double areaPerBar = Math.PI * dia * dia / 400.0; // cm²
                int capacityPerLayer = CalculateCapacityPerLayer(pos.Width, dia, settings);
                int absoluteMax = capacityPerLayer * maxLayers;
                int minRequired = pos.AsRequired > 0
                    ? (int)Math.Ceiling(pos.AsRequired / areaPerBar)
                    : 0;

                constraints.Add(new DiameterConstraint
                {
                    Diameter = dia,
                    AreaPerBar = areaPerBar,
                    CapacityPerLayer = capacityPerLayer,
                    AbsoluteMax = absoluteMax,
                    MinRequired = minRequired,
                    IsValid = minRequired <= absoluteMax
                });
            }

            return constraints;
        }

        private int CalculateCapacityPerLayer(double widthMM, int diameter, DtsSettings settings)
        {
            // N_capacity = floor((B - 2×Cover - 2×StirrupDia + Spacing) / (D + Spacing))
            double cover = settings.Beam?.CoverSide ?? 25;
            double stirrupDia = settings.Beam?.EstimatedStirrupDiameter ?? 10;
            double spacing = settings.Beam?.MinClearSpacing ?? 30;

            double usableWidth = widthMM - 2 * cover - 2 * stirrupDia;
            if (usableWidth <= 0) return 0;

            return (int)Math.Floor((usableWidth + spacing) / (diameter + spacing));
        }

        private List<int> FilterGloballyValidDiameters(
            List<PositionRequirement> positions,
            List<int> allDiameters)
        {
            // A diameter is globally invalid if it can't satisfy ANY position
            return allDiameters.Where(dia =>
            {
                foreach (var pos in positions)
                {
                    var constraint = pos.ValidDiameters.FirstOrDefault(c => c.Diameter == dia);
                    if (constraint != null && !constraint.IsValid)
                    {
                        // This diameter can't satisfy this position even with max layers
                        // But we should still allow it if other diameters can handle this position
                        // Only filter if NO diameter can handle this position
                    }
                }
                return true; // Keep all for now, prune later in addon phase
            }).ToList();
        }

        #endregion

        #region Phase 2: Local Addon Filling

        private AddonConfig FindBestAddonForPosition(
            PositionRequirement pos,
            double deficit,
            BackboneConfig backbone,
            DtsSettings settings,
            List<int> allowedDiameters)
        {
            AddonConfig bestAddon = null;
            double minWeight = double.MaxValue;

            int maxLayers = settings.Beam?.MaxLayers ?? 2;
            int backboneDia = pos.IsTop ? backbone.TopDiameter : backbone.BotDiameter;
            int backboneCount = pos.IsTop ? backbone.TopCount : backbone.BotCount;

            // Prefer addon diameter <= backbone diameter (construction rule)
            var addonDias = allowedDiameters
                .Where(d => d <= backboneDia || settings.Beam?.PreferSingleDiameter == false)
                .OrderByDescending(d => d == backboneDia) // Same diameter first
                .ThenBy(d => d)
                .ToList();

            foreach (int addonDia in addonDias)
            {
                double areaPerBar = Math.PI * addonDia * addonDia / 400.0;
                int minAddonBars = (int)Math.Ceiling(deficit / areaPerBar);
                int capacityPerLayer = CalculateCapacityPerLayer(pos.Width, addonDia, settings);

                // Try from minAddonBars up to some reasonable max
                for (int addonCount = minAddonBars; addonCount <= capacityPerLayer * maxLayers; addonCount++)
                {
                    // Determine which layer this addon goes to
                    int spaceOnLayer1 = capacityPerLayer - backboneCount;
                    int layer = DetermineAddonLayer(addonCount, spaceOnLayer1, capacityPerLayer, maxLayers);

                    if (layer < 0) continue; // Doesn't fit

                    // Pyramid rule: Layer n count <= Layer n-1 count
                    if (layer > 1 && addonCount > backboneCount) continue;

                    double weight = addonCount * areaPerBar * 0.1; // Simplified weight proxy
                    if (weight < minWeight)
                    {
                        minWeight = weight;
                        bestAddon = new AddonConfig
                        {
                            PositionId = pos.PositionId,
                            IsTop = pos.IsTop,
                            Diameter = addonDia,
                            Count = addonCount,
                            Layer = layer
                        };
                    }

                    // Found a valid solution, no need to try more counts for this diameter
                    break;
                }
            }

            return bestAddon;
        }

        private int DetermineAddonLayer(int addonCount, int spaceOnLayer1, int capacityPerLayer, int maxLayers)
        {
            if (addonCount <= spaceOnLayer1)
                return 1; // Fits on Layer 1 (same as backbone)

            int remaining = addonCount - spaceOnLayer1;
            if (remaining <= capacityPerLayer)
                return 2; // Layer 2

            if (maxLayers >= 3 && remaining <= 2 * capacityPerLayer)
                return 3; // Layer 3

            return -1; // Doesn't fit
        }

        private double CalculateAddonWeight(AddonConfig addon, double width, DtsSettings settings)
        {
            if (addon.Count == 0) return 0;

            // Simplified: assume addon length = some fraction of span
            double length = width * 2; // Rough estimate
            double volume = addon.Count * Math.PI * Math.Pow(addon.Diameter / 2.0, 2) * length / 1e9;
            return volume * 7850; // kg
        }

        #endregion

        #region Solution Creation

        private ContinuousBeamSolution CreateSolution(
            BackboneConfig backbone,
            List<AddonConfig> addons,
            BeamGroup group,
            DtsSettings settings)
        {
            var sol = new ContinuousBeamSolution
            {
                OptionName = backbone.ToId(),
                IsValid = true,
                BackboneDiameter_Top = backbone.TopDiameter,
                BackboneDiameter_Bot = backbone.BotDiameter,
                BackboneCount_Top = backbone.TopCount,
                BackboneCount_Bot = backbone.BotCount,
                Reinforcements = new Dictionary<string, RebarSpec>()
            };

            // Add addon specs to reinforcements
            foreach (var addon in addons.Where(a => a.Count > 0))
            {
                sol.Reinforcements[addon.PositionId] = new RebarSpec
                {
                    Diameter = addon.Diameter,
                    Count = addon.Count,
                    Layer = addon.Layer
                };
            }

            return sol;
        }

        #endregion
    }
}
