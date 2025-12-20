using System.Collections.Generic;
using System.Linq;
using DTS_Engine.Core.Algorithms;
using DTS_Engine.Core.Algorithms.Rebar.Models;
using DTS_Engine.Core.Algorithms.Rebar.Rules;
using DTS_Engine.Core.Data;

namespace DTS_Engine.Core.Algorithms.Rebar.Pipeline
{
    /// <summary>
    /// Pipeline orchestrator cho việc tính thép một dầm.
    /// Nhận seed context, chạy qua các stages, validate với RuleEngine.
    /// </summary>
    public class RebarPipeline
    {
        private readonly List<IRebarPipelineStage> _stages;
        private readonly RuleEngine _ruleEngine;

        public RebarPipeline(
            IEnumerable<IRebarPipelineStage> stages,
            RuleEngine ruleEngine)
        {
            _stages = stages.OrderBy(s => s.Order).ToList();
            _ruleEngine = ruleEngine;
        }

        public RebarPipeline() : this(Enumerable.Empty<IRebarPipelineStage>(), new RuleEngine())
        {
        }

        /// <summary>
        /// Execute pipeline cho 1 BeamGroup.
        /// </summary>
        /// <returns>Top 5 phương án xếp theo TotalScore</returns>
        public List<ContinuousBeamSolution> Execute(
            BeamGroup group,
            List<BeamResultData> spanResults,
            DtsSettings settings,
            ProjectConstraints globalConstraints,
            ExternalConstraints externalConstraints = null)
        {
            // Tạo seed context
            var seedContext = new SolutionContext
            {
                Group = group,
                SpanResults = spanResults,
                Settings = settings,
                GlobalConstraints = globalConstraints,
                ExternalConstraints = externalConstraints
            };

            // Pipeline bắt đầu với 1 context, stages có thể nhân bản (1 → N)
            IEnumerable<SolutionContext> contexts = new[] { seedContext };

            // Execute each stage
            foreach (var stage in _stages)
            {
                contexts = stage.Execute(contexts, globalConstraints);

                // Remove invalid contexts after each stage
                contexts = contexts.Where(c => c.IsValid).ToList();

                if (!contexts.Any())
                {
                    // Tất cả đều fail
                    return new List<ContinuousBeamSolution>();
                }
            }

            // Final validation with Rule Engine
            var validatedContexts = new List<SolutionContext>();
            foreach (var ctx in contexts)
            {
                _ruleEngine.ValidateAll(ctx);

                // Loại Critical, giữ Warning
                if (!ctx.HasCriticalError)
                {
                    validatedContexts.Add(ctx);
                }
            }

            // ═══════════════════════════════════════════════════════════════
            // SCORING (Migrated from V2)
            // ═══════════════════════════════════════════════════════════════

            var solutionsWithContext = validatedContexts
                .Where(c => c.CurrentSolution != null)
                .ToList();

            if (solutionsWithContext.Count > 0)
            {
                // Calculate Constructability Scores
                foreach (var ctx in solutionsWithContext)
                {
                    ctx.CurrentSolution.ConstructabilityScore =
                        ConstructabilityScoring.CalculateScore(ctx.CurrentSolution, ctx.Group, ctx.Settings);
                }

                // Normalize Weight Scores
                var weights = solutionsWithContext
                    .Select(c => c.CurrentSolution.TotalSteelWeight)
                    .Where(w => w > 0)
                    .ToList();

                double minW = weights.Count > 0 ? weights.Min() : 0;
                double maxW = weights.Count > 0 ? weights.Max() : 0;

                foreach (var ctx in solutionsWithContext)
                {
                    var sol = ctx.CurrentSolution;

                    // Calculate weight score (0-100, lower weight = higher score)
                    double weightScore;
                    if (weights.Count == 0 || (maxW - minW) < 0.001)
                    {
                        weightScore = 100; // All same weight or no weight data
                    }
                    else
                    {
                        weightScore = (maxW - sol.TotalSteelWeight) / (maxW - minW) * 100;
                    }
                    weightScore = System.Math.Max(0, System.Math.Min(100, weightScore));

                    double cs = System.Math.Max(0, System.Math.Min(100, sol.ConstructabilityScore));
                    sol.TotalScore = 0.6 * weightScore + 0.4 * cs;

                    // Apply penalty from Warning rules
                    sol.TotalScore -= ctx.TotalPenalty;
                    // Apply bonus from PreferredDiameter match
                    sol.TotalScore += ctx.PreferredDiameterBonus;
                }
            }

            // Rank by score and remove duplicates
            return solutionsWithContext
                .Select(c => c.CurrentSolution)
                .GroupBy(s => s.OptionName)
                .Select(g => g.OrderByDescending(x => x.TotalScore).First())
                .OrderByDescending(s => s.TotalScore)
                .ThenBy(s => s.TotalSteelWeight)
                .Take(5)
                .ToList();
        }

        /// <summary>
        /// Thêm stage vào pipeline.
        /// </summary>
        public void AddStage(IRebarPipelineStage stage)
        {
            _stages.Add(stage);
            _stages.Sort((a, b) => a.Order.CompareTo(b.Order));
        }
    }
}
