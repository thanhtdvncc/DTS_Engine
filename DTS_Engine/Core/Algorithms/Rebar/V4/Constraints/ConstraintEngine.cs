using System;
using System.Collections.Generic;
using System.Linq;
using DTS_Engine.Core.Data;

namespace DTS_Engine.Core.Algorithms.Rebar.V4.Constraints
{
    /// <summary>
    /// Registry và Executor cho tất cả constraints.
    /// Hỗ trợ đăng ký, kích hoạt/tắt, và thực thi constraints.
    /// 
    /// ISO 25010: Maintainability - Plugin architecture.
    /// ISO 12207: Configuration Management - Runtime extensibility.
    /// </summary>
    public class ConstraintEngine
    {
        #region Fields

        private readonly List<IArrangementConstraint> _arrangementConstraints = new List<IArrangementConstraint>();
        private readonly List<IBackboneConstraint> _backboneConstraints = new List<IBackboneConstraint>();
        private readonly List<ISolutionConstraint> _solutionConstraints = new List<ISolutionConstraint>();
        private readonly List<ISectionPairConstraint> _sectionPairConstraints = new List<ISectionPairConstraint>();

        private readonly DtsSettings _settings;

        #endregion

        #region Constructor

        /// <summary>
        /// Tạo ConstraintEngine với default constraints.
        /// </summary>
        public ConstraintEngine(DtsSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            // Register default constraints
            RegisterDefaultConstraints();
        }

        #endregion

        #region Registration

        /// <summary>
        /// Đăng ký constraint cho Arrangement.
        /// </summary>
        public void Register(IArrangementConstraint constraint)
        {
            if (constraint != null && !_arrangementConstraints.Any(c => c.Name == constraint.Name))
            {
                _arrangementConstraints.Add(constraint);
                SortByPriority(_arrangementConstraints);
            }
        }

        /// <summary>
        /// Đăng ký constraint cho Backbone.
        /// </summary>
        public void Register(IBackboneConstraint constraint)
        {
            if (constraint != null && !_backboneConstraints.Any(c => c.Name == constraint.Name))
            {
                _backboneConstraints.Add(constraint);
                SortByPriority(_backboneConstraints);
            }
        }

        /// <summary>
        /// Đăng ký constraint cho Solution.
        /// </summary>
        public void Register(ISolutionConstraint constraint)
        {
            if (constraint != null && !_solutionConstraints.Any(c => c.Name == constraint.Name))
            {
                _solutionConstraints.Add(constraint);
                SortByPriority(_solutionConstraints);
            }
        }

        /// <summary>
        /// Đăng ký constraint cho SectionPair.
        /// </summary>
        public void Register(ISectionPairConstraint constraint)
        {
            if (constraint != null && !_sectionPairConstraints.Any(c => c.Name == constraint.Name))
            {
                _sectionPairConstraints.Add(constraint);
                SortByPriority(_sectionPairConstraints);
            }
        }

        /// <summary>
        /// Gỡ bỏ constraint theo tên.
        /// </summary>
        public void Unregister(string constraintName)
        {
            _arrangementConstraints.RemoveAll(c => c.Name == constraintName);
            _backboneConstraints.RemoveAll(c => c.Name == constraintName);
            _solutionConstraints.RemoveAll(c => c.Name == constraintName);
            _sectionPairConstraints.RemoveAll(c => c.Name == constraintName);
        }

        /// <summary>
        /// Lấy danh sách tất cả constraints đã đăng ký.
        /// </summary>
        public List<IConstraint> GetAllConstraints()
        {
            var all = new List<IConstraint>();
            all.AddRange(_arrangementConstraints);
            all.AddRange(_backboneConstraints);
            all.AddRange(_solutionConstraints);
            all.AddRange(_sectionPairConstraints);
            return all.OrderBy(c => c.Priority).ToList();
        }

        #endregion

        #region Execution

        /// <summary>
        /// Kiểm tra arrangement với tất cả arrangement constraints.
        /// </summary>
        public List<ConstraintResult> CheckArrangement(SectionArrangement arrangement, DesignSection section)
        {
            var results = new List<ConstraintResult>();

            foreach (var constraint in _arrangementConstraints.Where(c => c.IsEnabled))
            {
                try
                {
                    var result = constraint.Check(arrangement, section);
                    results.Add(result);

                    // Fail-fast for Fatal
                    if (result.Severity == ConstraintSeverity.Fatal)
                        break;
                }
                catch (Exception ex)
                {
                    results.Add(ConstraintResult.Warning(
                        constraint.Name,
                        $"Exception: {ex.Message}",
                        0));
                }
            }

            return results;
        }

        /// <summary>
        /// Kiểm tra backbone candidate với tất cả backbone constraints.
        /// </summary>
        public List<ConstraintResult> CheckBackbone(BackboneCandidate candidate, List<DesignSection> sections)
        {
            var results = new List<ConstraintResult>();

            foreach (var constraint in _backboneConstraints.Where(c => c.IsEnabled))
            {
                try
                {
                    var result = constraint.Check(candidate, sections);
                    results.Add(result);

                    if (result.Severity == ConstraintSeverity.Fatal)
                        break;
                }
                catch (Exception ex)
                {
                    results.Add(ConstraintResult.Warning(
                        constraint.Name,
                        $"Exception: {ex.Message}",
                        0));
                }
            }

            return results;
        }

        /// <summary>
        /// Kiểm tra solution với tất cả solution constraints.
        /// </summary>
        public List<ConstraintResult> CheckSolution(ContinuousBeamSolution solution, BeamGroup group)
        {
            var results = new List<ConstraintResult>();

            foreach (var constraint in _solutionConstraints.Where(c => c.IsEnabled))
            {
                try
                {
                    var result = constraint.Check(solution, group);
                    results.Add(result);

                    if (result.Severity == ConstraintSeverity.Fatal)
                        break;
                }
                catch (Exception ex)
                {
                    results.Add(ConstraintResult.Warning(
                        constraint.Name,
                        $"Exception: {ex.Message}",
                        0));
                }
            }

            return results;
        }

        /// <summary>
        /// Kiểm tra cặp sections với tất cả section pair constraints.
        /// </summary>
        public List<ConstraintResult> CheckSectionPair(DesignSection left, DesignSection right)
        {
            var results = new List<ConstraintResult>();

            foreach (var constraint in _sectionPairConstraints.Where(c => c.IsEnabled))
            {
                try
                {
                    var result = constraint.Check(left, right);
                    results.Add(result);

                    if (result.Severity == ConstraintSeverity.Fatal)
                        break;
                }
                catch (Exception ex)
                {
                    results.Add(ConstraintResult.Warning(
                        constraint.Name,
                        $"Exception: {ex.Message}",
                        0));
                }
            }

            return results;
        }

        /// <summary>
        /// Kiểm tra xem có critical failure không.
        /// </summary>
        public bool HasCriticalFailure(List<ConstraintResult> results)
        {
            return results.Any(r => r.Severity >= ConstraintSeverity.Critical && !r.IsPassed);
        }

        /// <summary>
        /// Tính tổng penalty từ warnings.
        /// </summary>
        public double CalculateTotalPenalty(List<ConstraintResult> results)
        {
            return results
                .Where(r => r.Severity == ConstraintSeverity.Warning && !r.IsPassed)
                .Sum(r => r.Penalty);
        }

        #endregion

        #region Default Constraints

        /// <summary>
        /// Đăng ký các constraints mặc định.
        /// </summary>
        private void RegisterDefaultConstraints()
        {
            // Arrangement constraints
            Register(new MinBarsConstraint(_settings));
            Register(new MaxLayersConstraint(_settings));
            Register(new SpacingConstraint(_settings));

            // Backbone constraints
            Register(new DiameterUniformityConstraint(_settings));
            Register(new CountSymmetryConstraint(_settings));

            // Solution constraints
            Register(new SteelDeficitConstraint(_settings));
            Register(new MaxWeightConstraint(_settings));

            // Section pair constraints
            Register(new SupportContinuityConstraint(_settings));
        }

        private void SortByPriority<T>(List<T> list) where T : IConstraint
        {
            list.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        #endregion
    }

    #region Built-in Arrangement Constraints

    /// <summary>
    /// Constraint: Tối thiểu 2 thanh mỗi lớp.
    /// </summary>
    public class MinBarsConstraint : IArrangementConstraint
    {
        private readonly int _minBars;

        public MinBarsConstraint(DtsSettings settings)
        {
            _minBars = settings?.Beam?.MinBarsPerLayer ?? 2;
        }

        public string Name => "MinBars";
        public string Description => $"Tối thiểu {_minBars} thanh mỗi lớp";
        public int Priority => 1;
        public bool IsEnabled => true;
        public ConstraintType Type => ConstraintType.Arrangement;

        public ConstraintResult Check(SectionArrangement arrangement, DesignSection section)
        {
            if (arrangement.TotalCount == 0)
                return ConstraintResult.Pass(Name);

            foreach (var count in arrangement.BarsPerLayer)
            {
                if (count > 0 && count < _minBars)
                {
                    return ConstraintResult.Critical(
                        Name,
                        $"Lớp có {count} thanh < {_minBars} thanh tối thiểu",
                        $"Tăng lên {_minBars} thanh hoặc dùng đường kính lớn hơn");
                }
            }

            return ConstraintResult.Pass(Name);
        }
    }

    /// <summary>
    /// Constraint: Số lớp tối đa.
    /// </summary>
    public class MaxLayersConstraint : IArrangementConstraint
    {
        private readonly int _maxLayers;

        public MaxLayersConstraint(DtsSettings settings)
        {
            _maxLayers = settings?.Beam?.MaxLayers ?? 2;
        }

        public string Name => "MaxLayers";
        public string Description => $"Tối đa {_maxLayers} lớp thép";
        public int Priority => 2;
        public bool IsEnabled => true;
        public ConstraintType Type => ConstraintType.Arrangement;

        public ConstraintResult Check(SectionArrangement arrangement, DesignSection section)
        {
            if (arrangement.LayerCount > _maxLayers)
            {
                return ConstraintResult.Critical(
                    Name,
                    $"Cần {arrangement.LayerCount} lớp > {_maxLayers} lớp cho phép",
                    "Dùng đường kính lớn hơn hoặc tăng bề rộng dầm");
            }

            return ConstraintResult.Pass(Name);
        }
    }

    /// <summary>
    /// Constraint: Khoảng hở giữa các thanh.
    /// </summary>
    public class SpacingConstraint : IArrangementConstraint
    {
        private readonly double _minSpacing;
        private readonly double _maxSpacing;

        public SpacingConstraint(DtsSettings settings)
        {
            _minSpacing = settings?.Beam?.MinClearSpacing ?? 25;
            _maxSpacing = settings?.Beam?.MaxClearSpacing ?? 300;
        }

        public string Name => "Spacing";
        public string Description => $"Khoảng hở {_minSpacing}-{_maxSpacing}mm";
        public int Priority => 3;
        public bool IsEnabled => true;
        public ConstraintType Type => ConstraintType.Arrangement;

        public ConstraintResult Check(SectionArrangement arrangement, DesignSection section)
        {
            if (arrangement.TotalCount <= 1)
                return ConstraintResult.Pass(Name);

            if (arrangement.ClearSpacing < _minSpacing)
            {
                return ConstraintResult.Critical(
                    Name,
                    $"Khoảng hở {arrangement.ClearSpacing:F0}mm < {_minSpacing}mm tối thiểu",
                    "Giảm số thanh hoặc dùng đường kính nhỏ hơn");
            }

            if (arrangement.ClearSpacing > _maxSpacing)
            {
                return ConstraintResult.Warning(
                    Name,
                    $"Khoảng hở {arrangement.ClearSpacing:F0}mm > {_maxSpacing}mm (nứt?)",
                    5);
            }

            return ConstraintResult.Pass(Name);
        }
    }

    #endregion

    #region Built-in Backbone Constraints

    /// <summary>
    /// Constraint: Ưu tiên đường kính đồng nhất.
    /// </summary>
    public class DiameterUniformityConstraint : IBackboneConstraint
    {
        private readonly bool _prefer;

        public DiameterUniformityConstraint(DtsSettings settings)
        {
            _prefer = settings?.Beam?.PreferSingleDiameter ?? true;
        }

        public string Name => "DiameterUniformity";
        public string Description => "Ưu tiên đường kính đồng nhất";
        public int Priority => 10;
        public bool IsEnabled => _prefer;
        public ConstraintType Type => ConstraintType.Backbone;

        public ConstraintResult Check(BackboneCandidate candidate, List<DesignSection> sections)
        {
            // Backbone luôn đồng nhất theo thiết kế
            return ConstraintResult.Pass(Name);
        }
    }

    /// <summary>
    /// Constraint: Ưu tiên số thanh Top = Bot.
    /// </summary>
    public class CountSymmetryConstraint : IBackboneConstraint
    {
        public CountSymmetryConstraint(DtsSettings settings) { }

        public string Name => "CountSymmetry";
        public string Description => "Ưu tiên số thanh Top = Bot";
        public int Priority => 15;
        public bool IsEnabled => true;
        public ConstraintType Type => ConstraintType.Backbone;

        public ConstraintResult Check(BackboneCandidate candidate, List<DesignSection> sections)
        {
            if (candidate.CountTop != candidate.CountBot)
            {
                int diff = Math.Abs(candidate.CountTop - candidate.CountBot);
                return ConstraintResult.Warning(
                    Name,
                    $"Top={candidate.CountTop}, Bot={candidate.CountBot} (chênh {diff})",
                    diff * 2);
            }

            return ConstraintResult.Pass(Name);
        }
    }

    #endregion

    #region Built-in Solution Constraints

    /// <summary>
    /// Constraint: Kiểm tra đủ thép.
    /// </summary>
    public class SteelDeficitConstraint : ISolutionConstraint
    {
        private readonly double _tolerance;

        public SteelDeficitConstraint(DtsSettings settings)
        {
            _tolerance = settings?.Rules?.SafetyFactor ?? 0.98;
        }

        public string Name => "SteelDeficit";
        public string Description => "Kiểm tra đủ thép yêu cầu";
        public int Priority => 1;
        public bool IsEnabled => true;
        public ConstraintType Type => ConstraintType.Solution;

        public ConstraintResult Check(ContinuousBeamSolution solution, BeamGroup group)
        {
            // Check đã được thực hiện trong GlobalOptimizer.ValidateSolution
            if (!solution.IsValid && !string.IsNullOrEmpty(solution.ValidationMessage))
            {
                return ConstraintResult.Critical(
                    Name,
                    solution.ValidationMessage);
            }

            return ConstraintResult.Pass(Name);
        }
    }

    /// <summary>
    /// Constraint: Giới hạn trọng lượng thép.
    /// </summary>
    public class MaxWeightConstraint : ISolutionConstraint
    {
        private readonly double _maxWeightPerMeter;

        public MaxWeightConstraint(DtsSettings settings)
        {
            _maxWeightPerMeter = settings?.Beam?.MaxSteelWeightPerMeter ?? 50; // kg/m
        }

        public string Name => "MaxWeight";
        public string Description => $"Tối đa {_maxWeightPerMeter}kg/m";
        public int Priority => 20;
        public bool IsEnabled => _maxWeightPerMeter > 0;
        public ConstraintType Type => ConstraintType.Solution;

        public ConstraintResult Check(ContinuousBeamSolution solution, BeamGroup group)
        {
            double length = group?.TotalLength ?? 1;
            double weightPerMeter = solution.TotalSteelWeight / length;

            if (weightPerMeter > _maxWeightPerMeter)
            {
                return ConstraintResult.Warning(
                    Name,
                    $"Thép {weightPerMeter:F1}kg/m > {_maxWeightPerMeter}kg/m",
                    10);
            }

            return ConstraintResult.Pass(Name);
        }
    }

    #endregion

    #region Built-in Section Pair Constraints

    /// <summary>
    /// Constraint: Liên tục thép tại gối.
    /// </summary>
    public class SupportContinuityConstraint : ISectionPairConstraint
    {
        public SupportContinuityConstraint(DtsSettings settings) { }

        public string Name => "SupportContinuity";
        public string Description => "Thép gối phải liên tục qua cột";
        public int Priority => 5;
        public bool IsEnabled => true;
        public ConstraintType Type => ConstraintType.SectionPair;

        public ConstraintResult Check(DesignSection left, DesignSection right)
        {
            if (left == null || right == null)
                return ConstraintResult.Pass(Name);

            // Kiểm tra có ít nhất 1 phương án chung
            if (left.ValidArrangementsTop.Count == 0 || right.ValidArrangementsTop.Count == 0)
            {
                return ConstraintResult.Critical(
                    Name,
                    $"Không có phương án chung tại gối (L={left.SectionId}, R={right.SectionId})");
            }

            return ConstraintResult.Pass(Name);
        }
    }

    #endregion
}
