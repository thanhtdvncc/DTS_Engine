using DTS_Engine.Core.Algorithms.Rebar.Models;

namespace DTS_Engine.Core.Algorithms.Rebar.Rules
{
    /// <summary>
    /// Kiểm tra ưu tiên đối xứng: Số thanh chẵn được ưu tiên.
    /// WARNING: Vi phạm sẽ trừ điểm nhưng vẫn giữ phương án.
    /// </summary>
    public class SymmetryRule : IDesignRule
    {
        public string RuleName { get { return "Symmetry"; } }
        public int Priority { get { return 5; } } // Check later

        public ValidationResult Validate(SolutionContext ctx)
        {
            // Check if symmetry is preferred
            if (ctx.Settings?.Beam?.PreferSymmetric != true)
                return ValidationResult.Pass(RuleName);

            int nTop = ctx.TopBackboneCount;
            int nBot = ctx.BotBackboneCount;

            int oddCount = (nTop % 2) + (nBot % 2);

            if (oddCount > 0)
            {
                return ValidationResult.Warning(RuleName,
                    string.Format("Số thanh lẻ ({0}/{1}) - Không đối xứng hoàn hảo", nTop, nBot),
                    oddCount * 2.0); // Penalty: 2 điểm mỗi lớp lẻ
            }

            return ValidationResult.Pass(RuleName);
        }
    }
}
