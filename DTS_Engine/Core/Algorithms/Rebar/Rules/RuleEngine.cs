using System.Collections.Generic;
using System.Linq;
using DTS_Engine.Core.Algorithms.Rebar.Models;

namespace DTS_Engine.Core.Algorithms.Rebar.Rules
{
    /// <summary>
    /// Engine chạy tất cả Design Rules và tích lũy kết quả vào context.
    /// </summary>
    public class RuleEngine
    {
        private readonly List<IDesignRule> _rules;

        public RuleEngine(IEnumerable<IDesignRule> rules)
        {
            _rules = rules.OrderBy(r => r.Priority).ToList();
        }

        public RuleEngine() : this(Enumerable.Empty<IDesignRule>())
        {
        }

        /// <summary>
        /// Validate context với tất cả rules.
        /// Critical -> set IsValid = false và stop.
        /// Warning -> tích lũy TotalPenalty.
        /// </summary>
        public void ValidateAll(SolutionContext context)
        {
            foreach (var rule in _rules)
            {
                var result = rule.Validate(context);
                context.ValidationResults.Add(result);

                switch (result.Level)
                {
                    case SeverityLevel.Critical:
                        context.IsValid = false;
                        context.FailStage = $"Rule:{rule.RuleName}";
                        return; // Stop on first critical

                    case SeverityLevel.Warning:
                        context.TotalPenalty += result.PenaltyScore;
                        break;

                    case SeverityLevel.Info:
                    case SeverityLevel.Pass:
                        // No action
                        break;
                }
            }
        }

        /// <summary>
        /// Thêm rule mới (runtime injection).
        /// </summary>
        public void AddRule(IDesignRule rule)
        {
            _rules.Add(rule);
            _rules.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        /// <summary>
        /// Xóa rule theo tên.
        /// </summary>
        public bool RemoveRule(string ruleName)
        {
            return _rules.RemoveAll(r => r.RuleName == ruleName) > 0;
        }

        /// <summary>
        /// Lấy danh sách tên rules hiện tại.
        /// </summary>
        public IReadOnlyList<string> GetRuleNames()
        {
            return _rules.Select(r => r.RuleName).ToList();
        }
    }
}
