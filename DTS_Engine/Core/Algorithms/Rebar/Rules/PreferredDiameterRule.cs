using DTS_Engine.Core.Algorithms.Rebar.Models;

namespace DTS_Engine.Core.Algorithms.Rebar.Rules
{
    /// <summary>
    /// Kiểm tra đường kính ưu tiên từ ProjectConstraints.
    /// INFO: Gợi ý cải thiện nhưng không trừ điểm.
    /// </summary>
    public class PreferredDiameterRule : IDesignRule
    {
        public string RuleName { get { return "PreferredDiameter"; } }
        public int Priority { get { return 10; } } // Check last

        public ValidationResult Validate(SolutionContext ctx)
        {
            if (ctx.GlobalConstraints == null)
                return ValidationResult.Pass(RuleName);

            if (!ctx.GlobalConstraints.PreferredMainDiameter.HasValue)
                return ValidationResult.Pass(RuleName);

            int preferred = ctx.GlobalConstraints.PreferredMainDiameter.Value;
            bool topMatch = ctx.TopBackboneDiameter == preferred;
            bool botMatch = ctx.BotBackboneDiameter == preferred;

            if (topMatch && botMatch)
            {
                return ValidationResult.Info(RuleName,
                    string.Format("Đường kính trùng với ưu tiên dự án (D{0})", preferred));
            }

            if (topMatch || botMatch)
            {
                return ValidationResult.Info(RuleName,
                    string.Format("Một phần trùng với đường kính ưu tiên (D{0})", preferred));
            }

            // Not matching - no penalty, just info
            return ValidationResult.Pass(RuleName);
        }
    }
}
