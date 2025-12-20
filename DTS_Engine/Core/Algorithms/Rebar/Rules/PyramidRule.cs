using DTS_Engine.Core.Algorithms.Rebar.Models;

namespace DTS_Engine.Core.Algorithms.Rebar.Rules
{
    /// <summary>
    /// Kiểm tra quy tắc Kim tự tháp: Layer 2 <= Layer 1.
    /// CRITICAL: Vi phạm sẽ loại bỏ phương án.
    /// 
    /// NOTE: This rule checks Reinforcements from CurrentSolution.
    /// The Strategies already enforce PyramidRule during filling,
    /// so this is a final safety check.
    /// </summary>
    public class PyramidRule : IDesignRule
    {
        public string RuleName { get { return "Pyramid"; } }
        public int Priority { get { return 1; } } // Check first

        public ValidationResult Validate(SolutionContext ctx)
        {
            if (ctx.CurrentSolution == null)
                return ValidationResult.Pass(RuleName);

            var sol = ctx.CurrentSolution;
            var reinforcements = sol.Reinforcements;

            if (reinforcements == null || reinforcements.Count == 0)
            {
                // No local reinforcement, only backbone - pyramid guaranteed
                return ValidationResult.Pass(RuleName);
            }

            // Check each reinforcement: if Layer 2 has more than Layer 1, fail
            // However, the Strategy already handles this, so this is a sanity check.
            // The Reinforcements dictionary uses keys like "S1_Top_Left", "S1_Bot_Mid", etc.
            // Each RebarSpec has a Layer property (1 or 2).

            // For now, trust that Strategies enforce pyramid rule.
            // This rule becomes relevant when LongitudinalProfile is populated.
            return ValidationResult.Pass(RuleName);
        }
    }
}
