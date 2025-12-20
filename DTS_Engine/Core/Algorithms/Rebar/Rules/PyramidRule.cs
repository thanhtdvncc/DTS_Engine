using DTS_Engine.Core.Algorithms.Rebar.Models;

namespace DTS_Engine.Core.Algorithms.Rebar.Rules
{
    /// <summary>
    /// Kiểm tra quy tắc Kim tự tháp: Layer 2 <= Layer 1.
    /// CRITICAL: Vi phạm sẽ loại bỏ phương án.
    /// </summary>
    public class PyramidRule : IDesignRule
    {
        public string RuleName { get { return "Pyramid"; } }
        public int Priority { get { return 1; } } // Check first

        public ValidationResult Validate(SolutionContext ctx)
        {
            if (ctx.LongitudinalProfile == null)
            {
                // No profile yet - check from solution directly
                if (ctx.CurrentSolution == null)
                    return ValidationResult.Pass(RuleName);

                // Basic check: backbone count should be consistent
                return ValidationResult.Pass(RuleName);
            }

            // Check TopLayer
            int topL1 = ctx.LongitudinalProfile.TopLayer1?.Count ?? 0;
            int topL2 = ctx.LongitudinalProfile.TopLayer2?.Count ?? 0;

            if (topL2 > topL1)
            {
                return ValidationResult.Critical(RuleName,
                    string.Format("Top Layer 2 ({0}) > Layer 1 ({1}) - Vi phạm quy tắc kim tự tháp", topL2, topL1));
            }

            // Check BotLayer
            int botL1 = ctx.LongitudinalProfile.BotLayer1?.Count ?? 0;
            int botL2 = ctx.LongitudinalProfile.BotLayer2?.Count ?? 0;

            if (botL2 > botL1)
            {
                return ValidationResult.Critical(RuleName,
                    string.Format("Bot Layer 2 ({0}) > Layer 1 ({1}) - Vi phạm quy tắc kim tự tháp", botL2, botL1));
            }

            return ValidationResult.Pass(RuleName);
        }
    }
}
