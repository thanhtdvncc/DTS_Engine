using System;
using System.Collections.Generic;
using System.Linq;

namespace DTS_Engine.Core.Algorithms.Rebar.V4.Constraints
{
    /// <summary>
    /// Mức độ nghiêm trọng của constraint violation.
    /// ISO 25010: Reliability - Clear severity classification.
    /// </summary>
    public enum ConstraintSeverity
    {
        /// <summary>Thông tin, không ảnh hưởng kết quả</summary>
        Info = 0,

        /// <summary>Cảnh báo, trừ điểm nhưng vẫn chấp nhận</summary>
        Warning = 1,

        /// <summary>Nghiêm trọng, loại bỏ phương án</summary>
        Critical = 2,

        /// <summary>Lỗi hệ thống, dừng tính toán</summary>
        Fatal = 3
    }

    /// <summary>
    /// Kết quả kiểm tra constraint.
    /// </summary>
    public class ConstraintResult
    {
        /// <summary>Constraint đã pass?</summary>
        public bool IsPassed { get; set; }

        /// <summary>Mức độ nghiêm trọng nếu fail</summary>
        public ConstraintSeverity Severity { get; set; }

        /// <summary>Tên constraint</summary>
        public string ConstraintName { get; set; }

        /// <summary>Thông điệp chi tiết</summary>
        public string Message { get; set; }

        /// <summary>Điểm trừ (chỉ áp dụng cho Warning)</summary>
        public double Penalty { get; set; }

        /// <summary>Gợi ý khắc phục</summary>
        public string SuggestedFix { get; set; }

        #region Factory Methods

        public static ConstraintResult Pass(string constraintName)
        {
            return new ConstraintResult
            {
                IsPassed = true,
                Severity = ConstraintSeverity.Info,
                ConstraintName = constraintName
            };
        }

        public static ConstraintResult Info(string constraintName, string message)
        {
            return new ConstraintResult
            {
                IsPassed = true,
                Severity = ConstraintSeverity.Info,
                ConstraintName = constraintName,
                Message = message
            };
        }

        public static ConstraintResult Warning(string constraintName, string message, double penalty = 5)
        {
            return new ConstraintResult
            {
                IsPassed = false,
                Severity = ConstraintSeverity.Warning,
                ConstraintName = constraintName,
                Message = message,
                Penalty = penalty
            };
        }

        public static ConstraintResult Critical(string constraintName, string message, string fix = null)
        {
            return new ConstraintResult
            {
                IsPassed = false,
                Severity = ConstraintSeverity.Critical,
                ConstraintName = constraintName,
                Message = message,
                SuggestedFix = fix
            };
        }

        public static ConstraintResult Fatal(string constraintName, string message)
        {
            return new ConstraintResult
            {
                IsPassed = false,
                Severity = ConstraintSeverity.Fatal,
                ConstraintName = constraintName,
                Message = message
            };
        }

        #endregion

        public override string ToString()
        {
            return $"[{Severity}] {ConstraintName}: {(IsPassed ? "PASS" : Message)}";
        }
    }

    /// <summary>
    /// Interface cho tất cả constraints.
    /// Hỗ trợ mở rộng dễ dàng theo Open/Closed Principle (SOLID).
    /// ISO 12207: Extensibility through abstraction.
    /// </summary>
    public interface IConstraint
    {
        /// <summary>Tên constraint (unique identifier)</summary>
        string Name { get; }

        /// <summary>Mô tả ngắn</summary>
        string Description { get; }

        /// <summary>Thứ tự ưu tiên (thấp = check trước)</summary>
        int Priority { get; }

        /// <summary>Constraint này có enabled không?</summary>
        bool IsEnabled { get; }

        /// <summary>Loại constraint</summary>
        ConstraintType Type { get; }
    }

    /// <summary>
    /// Loại constraint (áp dụng ở giai đoạn nào).
    /// </summary>
    public enum ConstraintType
    {
        /// <summary>Áp dụng cho từng section riêng lẻ</summary>
        Section = 0,

        /// <summary>Áp dụng cho cặp sections (VD: Support Left-Right)</summary>
        SectionPair = 1,

        /// <summary>Áp dụng cho arrangement</summary>
        Arrangement = 2,

        /// <summary>Áp dụng cho backbone candidate</summary>
        Backbone = 3,

        /// <summary>Áp dụng cho solution cuối cùng</summary>
        Solution = 4,

        /// <summary>Áp dụng toàn cục (cross-beam)</summary>
        Global = 5
    }

    /// <summary>
    /// Constraint kiểm tra SectionArrangement.
    /// VD: Min bars, Max layers, Spacing check.
    /// </summary>
    public interface IArrangementConstraint : IConstraint
    {
        /// <summary>
        /// Kiểm tra arrangement có hợp lệ không.
        /// </summary>
        ConstraintResult Check(SectionArrangement arrangement, DesignSection section);
    }

    /// <summary>
    /// Constraint kiểm tra BackboneCandidate.
    /// VD: Diameter uniformity, Count symmetry.
    /// </summary>
    public interface IBackboneConstraint : IConstraint
    {
        /// <summary>
        /// Kiểm tra backbone candidate có hợp lệ không.
        /// </summary>
        ConstraintResult Check(BackboneCandidate candidate, List<DesignSection> sections);
    }

    /// <summary>
    /// Constraint kiểm tra ContinuousBeamSolution.
    /// VD: Total weight, Constructability, Code compliance.
    /// </summary>
    public interface ISolutionConstraint : IConstraint
    {
        /// <summary>
        /// Kiểm tra solution có hợp lệ không.
        /// </summary>
        ConstraintResult Check(Data.ContinuousBeamSolution solution, Data.BeamGroup group);
    }

    /// <summary>
    /// Constraint kiểm tra cặp DesignSection (Support pair).
    /// </summary>
    public interface ISectionPairConstraint : IConstraint
    {
        /// <summary>
        /// Kiểm tra cặp sections có tương thích không.
        /// </summary>
        ConstraintResult Check(DesignSection left, DesignSection right);
    }
}
