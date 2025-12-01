using System;
using System.Collections.Generic;
using System.Linq;
using DTS_Wall_Tool.Core.Algorithms;
using DTS_Wall_Tool.Core.Data;
using DTS_Wall_Tool.Core.Primitives;

namespace DTS_Wall_Tool.Core.Engines
{
    /// <summary>
    /// Kết quả mapping cho một tường
    /// </summary>
    public class MappingResult
    {
        public string WallHandle { get; set; }
        public double WallLength { get; set; }
        public List<MappingRecord> Mappings { get; set; } = new List<MappingRecord>();

        // Visual Fill - vẽ đường màu đè lên để kỹ sư nhìn vào biết đoạn nào được gán tải
        public List<VisualSegment> VisualSegments { get; set; } = new List<VisualSegment>();

        public double CoveredLength => Mappings.Where(m => m.TargetFrame != "New").Sum(m => m.CoveredLength);
        public double CoveragePercent => WallLength > 0 ? (CoveredLength / WallLength) * 100 : 0;
        public bool IsFullyCovered => CoveragePercent >= 95.0;
        public bool HasMapping => Mappings.Count > 0 && Mappings.Any(m => m.TargetFrame != "New");

        /// <summary>
        /// Xác định màu hiển thị cho tường:
        /// - Xanh (3): Full coverage (>=95%)
        /// - Vàng (2): Partial coverage
        /// - Đỏ (1): No match -> NEW
        /// - Magenta (6): Has Override (user edited)
        /// </summary>
        public int GetColorIndex(bool hasOverride = false)
        {
            if (hasOverride) return 6; // Magenta - User Override
            if (!HasMapping) return 1; // Red - New
            if (IsFullyCovered) return 3; // Green - Full
            return 2; // Yellow - Partial
        }

        public string GetLabelText(string wallType, string loadPattern, double loadValue)
        {
            string loadStr = $"{wallType} {loadPattern}={loadValue:0.00}";

            if (Mappings.Count == 0 || !HasMapping)
                return loadStr + " -> New";

            if (Mappings.Count == 1)
            {
                var m = Mappings[0];
                if (m.MatchType == "FULL")
                    return loadStr + $" -> {m.TargetFrame} (full {m.FrameLength / 1000:0. 0}m)";
                else
                    return loadStr + $" -> {m.TargetFrame} I={m.DistI / 1000:0.0}to{m.DistJ / 1000:0.0}";
            }

            var frameNames = Mappings.Select(m => m.TargetFrame).Distinct();
            return loadStr + " -> " + string.Join(", ", frameNames);
        }
    }

    /// <summary>
    /// Đoạn visual để vẽ đường màu trên CAD
    /// </summary>
    public class VisualSegment
    {
        public Point2D Start { get; set; }
        public Point2D End { get; set; }
        public string FrameName { get; set; }
        public string MatchType { get; set; }
        public int ColorIndex { get; set; } = 3; // Default green
    }

    /// <summary>
    /// Candidate frame sau khi lọc sơ bộ
    /// </summary>
    internal class FrameCandidate
    {
        public SapFrame Frame { get; set; }
        public double OverlapLength { get; set; }
        public double PerpDist { get; set; }
        public double Score { get; set; }

        // Projection results (local coordinate on frame axis)
        public double WallProjStart { get; set; }  // Wall start projected on frame axis
        public double WallProjEnd { get; set; }    // Wall end projected on frame axis
        public double OverlapStart { get; set; }   // Overlap start on frame [0, L]
        public double OverlapEnd { get; set; }     // Overlap end on frame [0, L]
    }

    /// <summary>
    /// Engine mapping tường lên dầm SAP2000
    /// Chiến thuật: "Hình chiếu & Chồng lấn" (Projection & Overlap)
    /// 
    /// Quy trình:
    /// 1. Sàng lọc thô: Loại cột, lọc theo Z, lọc theo góc, lọc theo khoảng cách
    /// 2.  Hệ trục địa phương: Coi dầm là trục số [0, L], chiếu tường lên trục
    /// 3.  Tính toán chồng lấn: Tìm giao [Wall] ∩ [0, L]
    /// 4. Phân loại: FULL / PARTIAL / NEW
    /// </summary>
    public static class MappingEngine
    {
        #region Configuration (Tunable Parameters)

        /// <summary>Dung sai cao độ Z (mm)</summary>
        public static double TOLERANCE_Z = 200.0;

        /// <summary>Dung sai khoảng cách vuông góc (mm) - có thể tính động theo chiều dày tường</summary>
        public static double TOLERANCE_DIST = 300.0;

        /// <summary>Chiều dài overlap tối thiểu để chấp nhận (mm)</summary>
        public static double MIN_OVERLAP = 100.0;

        /// <summary>Dung sai góc song song (rad) ~ 10 độ</summary>
        public static double TOLERANCE_ANGLE = 10.0 * GeometryConstants.DEG_TO_RAD;

        /// <summary>Tỷ lệ overlap tối thiểu để xem là match hợp lệ (15%)</summary>
        public static double MIN_OVERLAP_RATIO = 0.15;

        /// <summary>Gap tối đa cho phép để thực hiện Gap Match (mm)</summary>
        public static double MAX_GAP_DISTANCE = 2000.0;

        #endregion

        #region Main Mapping API

        /// <summary>
        /// Tìm tất cả dầm đỡ một tường (Main Entry Point)
        /// </summary>
        /// <param name="wallStart">Điểm đầu tường (CAD coordinates)</param>
        /// <param name="wallEnd">Điểm cuối tường (CAD coordinates)</param>
        /// <param name="wallZ">Cao độ Z của tường (mm)</param>
        /// <param name="frames">Danh sách dầm từ SAP2000</param>
        /// <param name="insertionOffset">Offset để chuyển từ CAD sang SAP coordinates</param>
        /// <param name="wallThickness">Chiều dày tường để tính dynamic tolerance</param>
        /// <returns>Kết quả mapping</returns>
        public static MappingResult FindMappings(
            Point2D wallStart,
            Point2D wallEnd,
            double wallZ,
            IEnumerable<SapFrame> frames,
            Point2D insertionOffset = default,
            double wallThickness = 200.0)
        {
            var result = new MappingResult
            {
                WallLength = wallStart.DistanceTo(wallEnd)
            };

            // Validate input
            if (result.WallLength < GeometryConstants.EPSILON)
                return result;

            // Apply coordinate offset (CAD -> SAP)
            var wStart = new Point2D(wallStart.X - insertionOffset.X, wallStart.Y - insertionOffset.Y);
            var wEnd = new Point2D(wallEnd.X - insertionOffset.X, wallEnd.Y - insertionOffset.Y);
            var wallSeg = new LineSegment2D(wStart, wEnd);

            // Calculate dynamic distance tolerance based on wall thickness
            double dynamicDistTol = CalculateDynamicDistanceTolerance(wallThickness);

            // ========== PHASE 1: PRE-FILTER ==========
            var frameList = frames.ToList();
            var candidates = new List<FrameCandidate>();

            foreach (var frame in frameList)
            {
                // Skip columns
                if (frame.IsVertical) continue;

                // Filter by Z elevation
                if (!IsElevationMatch(frame, wallZ)) continue;

                var frameSeg = new LineSegment2D(frame.StartPt, frame.EndPt);

                // Filter by parallel angle (allow opposite direction)
                if (!IsParallelOrOpposite(wallSeg.Angle, frameSeg.Angle)) continue;

                // Filter by perpendicular distance
                double perpDist = DistanceAlgorithms.BetweenParallelSegments(wallSeg, frameSeg);
                if (perpDist > dynamicDistTol) continue;

                // ========== PHASE 2: LOCAL PROJECTION ==========
                // Project wall endpoints onto frame axis [0, L]
                var projResult = ProjectWallOntoFrame(wallSeg, frame);

                // ========== PHASE 3: OVERLAP CALCULATION ==========
                double frameLen = frame.Length2D;
                double t1 = projResult.WallProjStart;
                double t2 = projResult.WallProjEnd;

                // Ensure t1 < t2
                if (t1 > t2) (t1, t2) = (t2, t1);

                // Calculate intersection with [0, frameLen]
                double overlapStart = Math.Max(t1, 0);
                double overlapEnd = Math.Min(t2, frameLen);
                double overlapLen = overlapEnd - overlapStart;

                // ========== PHASE 4: DECISION LOGIC ==========
                bool isValidMatch = false;
                double gap = 0;

                // PRIORITY A: Physical Overlap
                if (overlapLen > MIN_OVERLAP)
                {
                    double overlapRatio = overlapLen / result.WallLength;
                    if (overlapRatio >= MIN_OVERLAP_RATIO)
                    {
                        isValidMatch = true;
                    }
                }

                // PRIORITY B: Gap Match (only if no overlap)
                if (!isValidMatch)
                {
                    // Calculate gap
                    if (t2 < 0)
                        gap = -t2; // Wall before frame
                    else if (t1 > frameLen)
                        gap = t1 - frameLen; // Wall after frame
                    else
                        gap = 0;

                    if (gap > 0 && gap <= MAX_GAP_DISTANCE)
                    {
                        // Strict length check for gap match (prevent orphan suction)
                        double lenDiff = Math.Abs(frameLen - result.WallLength);
                        double lenRatio = result.WallLength / frameLen;

                        bool isSimilarLength = (lenDiff <= 1000) || (lenRatio >= 0.7 && lenRatio <= 1.3);

                        if (isSimilarLength)
                        {
                            isValidMatch = true;
                            // Set nominal overlap for gap match
                            if (overlapLen <= 0)
                            {
                                overlapLen = 100;
                                if (t2 < 0)
                                {
                                    overlapStart = 0;
                                    overlapEnd = 100;
                                }
                                else
                                {
                                    overlapStart = frameLen - 100;
                                    overlapEnd = frameLen;
                                }
                            }
                        }
                    }
                }

                if (!isValidMatch) continue;

                // Calculate score
                double distPenalty = perpDist / dynamicDistTol;
                if (distPenalty > 1) distPenalty = 1;
                double overlapRatioFinal = Math.Max(overlapLen / result.WallLength, 0);
                double score = (overlapRatioFinal * 0.7) + ((1 - distPenalty) * 0.3);

                candidates.Add(new FrameCandidate
                {
                    Frame = frame,
                    OverlapLength = overlapLen,
                    PerpDist = perpDist,
                    Score = score,
                    WallProjStart = t1,
                    WallProjEnd = t2,
                    OverlapStart = overlapStart,
                    OverlapEnd = overlapEnd
                });
            }

            // No candidates found -> NEW
            if (candidates.Count == 0)
            {
                result.Mappings.Add(CreateNewMapping(result.WallLength));
                return result;
            }

            // Sort by score (best first)
            candidates = candidates.OrderByDescending(c => c.Score).ToList();

            // ========== PHASE 5: GENERATE MAPPING RECORDS ==========
            foreach (var candidate in candidates)
            {
                var mapping = CreateMappingFromCandidate(candidate, wallSeg, result.WallLength);
                if (mapping != null)
                {
                    result.Mappings.Add(mapping);

                    // Create visual segment
                    var visual = CreateVisualSegment(candidate, wallSeg);
                    if (visual != null)
                        result.VisualSegments.Add(visual);
                }
            }

            // Optimize mappings (remove duplicates, prefer FULL match)
            result.Mappings = OptimizeMappings(result.Mappings, result.WallLength);

            return result;
        }

        /// <summary>
        /// Overload for LineSegment2D input
        /// </summary>
        public static MappingResult FindMappings(LineSegment2D wallSegment, double wallZ,
            IEnumerable<SapFrame> frames, Point2D insertionOffset = default, double wallThickness = 200.0)
        {
            return FindMappings(wallSegment.Start, wallSegment.End, wallZ, frames, insertionOffset, wallThickness);
        }

        #endregion

        #region Core Algorithms

        /// <summary>
        /// Check if frame elevation matches wall Z
        /// </summary>
        private static bool IsElevationMatch(SapFrame frame, double wallZ)
        {
            double frameZ = Math.Min(frame.Z1, frame.Z2);
            return Math.Abs(frameZ - wallZ) <= TOLERANCE_Z;
        }

        /// <summary>
        /// Check if two angles are parallel (same or opposite direction)
        /// </summary>
        private static bool IsParallelOrOpposite(double angle1, double angle2)
        {
            double diff = Math.Abs(angle1 - angle2);
            if (diff > Math.PI) diff = 2 * Math.PI - diff;

            // Same direction
            if (diff <= TOLERANCE_ANGLE) return true;

            // Opposite direction (180 degrees)
            if (Math.Abs(diff - Math.PI) <= TOLERANCE_ANGLE) return true;

            return false;
        }

        /// <summary>
        /// Calculate dynamic distance tolerance based on wall thickness
        /// From VBA: dynamicDistTol = wallThickness * 5, clamped to [250, 1500]
        /// </summary>
        private static double CalculateDynamicDistanceTolerance(double wallThickness)
        {
            double tol = wallThickness * 5.0;
            if (tol < 250) tol = 250;
            if (tol > 1500) tol = 1500;
            return tol;
        }

        /// <summary>
        /// Project wall endpoints onto frame local axis
        /// Frame axis: Start = 0, End = Length
        /// </summary>
        private static (double WallProjStart, double WallProjEnd) ProjectWallOntoFrame(LineSegment2D wallSeg, SapFrame frame)
        {
            // Frame unit vector
            double frameLen = frame.Length2D;
            if (frameLen < GeometryConstants.EPSILON)
                return (0, 0);

            double ux = (frame.EndPt.X - frame.StartPt.X) / frameLen;
            double uy = (frame.EndPt.Y - frame.StartPt.Y) / frameLen;

            // Project wall start
            double t1 = (wallSeg.Start.X - frame.StartPt.X) * ux + (wallSeg.Start.Y - frame.StartPt.Y) * uy;

            // Project wall end
            double t2 = (wallSeg.End.X - frame.StartPt.X) * ux + (wallSeg.End.Y - frame.StartPt.Y) * uy;

            return (t1, t2);
        }

        /// <summary>
        /// Create NEW mapping record
        /// </summary>
        private static MappingRecord CreateNewMapping(double length)
        {
            return new MappingRecord
            {
                TargetFrame = "New",
                MatchType = "NEW",
                CoveredLength = length,
                DistI = 0,
                DistJ = length,
                FrameLength = length
            };
        }

        /// <summary>
        /// Create mapping record from candidate
        /// </summary>
        private static MappingRecord CreateMappingFromCandidate(FrameCandidate candidate, LineSegment2D wallSeg, double wallLength)
        {
            double frameLen = candidate.Frame.Length2D;

            // Calculate DistI and DistJ (absolute distance on frame)
            double distI = Math.Round(candidate.OverlapStart, 0);
            double distJ = Math.Round(candidate.OverlapEnd, 0);

            // Clamp to valid range
            if (distI < 0) distI = 0;
            if (distJ > frameLen) distJ = frameLen;

            // Determine match type
            string matchType = "PARTIAL";
            if (distI < 1 && Math.Abs(distJ - frameLen) < 1)
                matchType = "FULL";
            else if (candidate.OverlapLength >= wallLength * 0.95)
                matchType = "FULL";

            // Boost score for FULL match
            double finalScore = candidate.Score;
            if (matchType == "FULL") finalScore += 0.5;

            return new MappingRecord
            {
                TargetFrame = candidate.Frame.Name,
                MatchType = matchType,
                DistI = distI,
                DistJ = distJ,
                FrameLength = frameLen,
                CoveredLength = candidate.OverlapLength
            };
        }

        /// <summary>
        /// Create visual segment for CAD display
        /// </summary>
        private static VisualSegment CreateVisualSegment(FrameCandidate candidate, LineSegment2D wallSeg)
        {
            double frameLen = candidate.Frame.Length2D;
            if (frameLen < GeometryConstants.EPSILON) return null;

            // Frame unit vector
            double ux = (candidate.Frame.EndPt.X - candidate.Frame.StartPt.X) / frameLen;
            double uy = (candidate.Frame.EndPt.Y - candidate.Frame.StartPt.Y) / frameLen;

            // Calculate visual start/end points on frame
            var visStart = new Point2D(
                candidate.Frame.StartPt.X + candidate.OverlapStart * ux,
                candidate.Frame.StartPt.Y + candidate.OverlapStart * uy
            );
            var visEnd = new Point2D(
                candidate.Frame.StartPt.X + candidate.OverlapEnd * ux,
                candidate.Frame.StartPt.Y + candidate.OverlapEnd * uy
            );

            return new VisualSegment
            {
                Start = visStart,
                End = visEnd,
                FrameName = candidate.Frame.Name,
                MatchType = candidate.OverlapLength >= wallSeg.Length * 0.95 ? "FULL" : "PARTIAL",
                ColorIndex = 3 // Green
            };
        }

        /// <summary>
        /// Optimize mappings: remove duplicates, prefer FULL match
        /// </summary>
        private static List<MappingRecord> OptimizeMappings(List<MappingRecord> mappings, double wallLength)
        {
            if (mappings.Count <= 1)
                return mappings;

            // Remove duplicate frame names (keep first = best score)
            var unique = mappings
                .GroupBy(m => m.TargetFrame)
                .Select(g => g.First())
                .ToList();

            // If a FULL match covers almost entire wall, use only that
            var fullCoverage = unique.FirstOrDefault(m => m.MatchType == "FULL");
            if (fullCoverage != null && fullCoverage.CoveredLength >= wallLength * 0.95)
            {
                return new List<MappingRecord> { fullCoverage };
            }

            return unique;
        }

        #endregion

        #region Debug/Logging Support

        /// <summary>
        /// Get detailed analysis for debugging
        /// </summary>
        public static string GetAnalysisReport(MappingResult result)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== MAPPING ANALYSIS ===");
            sb.AppendLine($"Wall Length: {result.WallLength:0.0} mm");
            sb.AppendLine($"Covered: {result.CoveredLength:0. 0} mm ({result.CoveragePercent:0.0}%)");
            sb.AppendLine($"Has Mapping: {result.HasMapping}");
            sb.AppendLine($"Is Fully Covered: {result.IsFullyCovered}");
            sb.AppendLine($"Color Index: {result.GetColorIndex()}");
            sb.AppendLine($"Mappings ({result.Mappings.Count}):");
            foreach (var m in result.Mappings)
            {
                sb.AppendLine($"  - {m}");
            }
            return sb.ToString();
        }

        #endregion
    }
}