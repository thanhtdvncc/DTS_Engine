namespace DTS_Engine.Core.Algorithms.Rebar.Rules
{
    /// <summary>
    /// Mức độ nghiêm trọng của vi phạm rule.
    /// </summary>
    public enum SeverityLevel
    {
        /// <summary>Không vi phạm</summary>
        Pass = 0,

        /// <summary>Gợi ý cải thiện (không trừ điểm)</summary>
        Info = 1,

        /// <summary>Chấp nhận được nhưng trừ điểm</summary>
        Warning = 2,

        /// <summary>Loại bỏ ngay - vi phạm tiêu chuẩn</summary>
        Critical = 3
    }

    /// <summary>
    /// Kết quả validate một rule.
    /// </summary>
    public class ValidationResult
    {
        public SeverityLevel Level { get; set; }
        public string RuleName { get; set; }
        public string Message { get; set; }

        /// <summary>
        /// Điểm trừ khi vi phạm (0-100, chỉ áp dụng cho Warning).
        /// </summary>
        public double PenaltyScore { get; set; }

        public static ValidationResult Pass(string ruleName)
        {
            return new ValidationResult { Level = SeverityLevel.Pass, RuleName = ruleName };
        }

        public static ValidationResult Info(string ruleName, string message)
        {
            return new ValidationResult { Level = SeverityLevel.Info, RuleName = ruleName, Message = message };
        }

        public static ValidationResult Warning(string ruleName, string message, double penalty)
        {
            return new ValidationResult { Level = SeverityLevel.Warning, RuleName = ruleName, Message = message, PenaltyScore = penalty };
        }

        public static ValidationResult Critical(string ruleName, string message)
        {
            return new ValidationResult { Level = SeverityLevel.Critical, RuleName = ruleName, Message = message };
        }
    }

    /// <summary>
    /// Interface cho Design Rules.
    /// </summary>
    public interface IDesignRule
    {
        /// <summary>
        /// Tên rule (dùng để logging).
        /// </summary>
        string RuleName { get; }

        /// <summary>
        /// Thứ tự check (1 = high priority, check trước).
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// Validate context và trả về kết quả với severity level.
        /// </summary>
        ValidationResult Validate(Models.SolutionContext context);
    }
}
