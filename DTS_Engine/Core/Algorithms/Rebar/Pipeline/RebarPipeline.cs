using System.Collections.Generic;
using System.Linq;
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
                    System.Diagnostics.Debug.WriteLine($"[RebarPipeline] All contexts failed at stage: {stage.StageName}");
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

            // Rank by score (accounting for penalties and bonuses)
            return validatedContexts
                .Where(c => c.CurrentSolution != null)
                .Select(c =>
                {
                    // Apply penalty from Warning rules
                    c.CurrentSolution.TotalScore -= c.TotalPenalty;
                    // Apply bonus from PreferredDiameter match
                    c.CurrentSolution.TotalScore += c.PreferredDiameterBonus;
                    return c.CurrentSolution;
                })
                .OrderByDescending(s => s.TotalScore)
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
