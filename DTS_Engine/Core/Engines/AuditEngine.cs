using DTS_Engine.Core.Data;
using DTS_Engine.Core.Primitives;
using DTS_Engine.Core.Utils;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DTS_Engine.Core.Engines
{
    /// <summary>
    /// Engine kiểm toán tải trọng từ SAP2000.
    /// Tự động gom nhóm theo Tầng -> Loại tải -> Giá trị.
    /// Sử dụng NetTopologySuite để union và slice geometry.
    /// 
    /// Quy trình xử lý:
    /// 1. Data Extraction: Lấy toàn bộ tải trọng từ SAP2000 API
    /// 2. Spatial Mapping: Xác định tầng và vị trí trục
    /// 3. Grouping: Gom nhóm theo tầng, loại tải, giá trị
    /// 4. Calculation: Tính diện tích/chiều dài và tổng lực
    /// 5. Reporting: Xuất báo cáo định dạng kỹ sư
    /// </summary>
    public class AuditEngine
    {
        #region Constants

        /// <summary>Dung sai Z để xác định cùng tầng (mm)</summary>
        private const double STORY_TOLERANCE = 500.0;

        /// <summary>Dung sai giá trị tải để gom nhóm (%)</summary>
        private const double VALUE_TOLERANCE_PERCENT = 1.0;

        /// <summary>Hệ số quy đổi mm² sang m²</summary>
        private const double MM2_TO_M2 = 1.0 / 1000000.0;

        /// <summary>Hệ số quy đổi mm sang m</summary>
        private const double MM_TO_M = 1.0 / 1000.0;

        #endregion

        #region Fields

        private List<SapUtils.GridLineRecord> _grids;
        private List<SapUtils.GridStoryItem> _stories;
        private Dictionary<string, SapFrame> _frameGeometryCache;
        private Dictionary<string, SapArea> _areaGeometryCache;
        private GeometryFactory _geometryFactory;

        #endregion

        #region Constructor

        public AuditEngine()
        {
            _geometryFactory = new GeometryFactory();

            // Cache grids và stories từ SAP
            if (SapUtils.IsConnected)
            {
                _grids = SapUtils.GetGridLines();
                _stories = SapUtils.GetStories();
            }
            else
            {
                _grids = new List<SapUtils.GridLineRecord>();
                _stories = new List<SapUtils.GridStoryItem>();
            }

            _frameGeometryCache = new Dictionary<string, SapFrame>();
            _areaGeometryCache = new Dictionary<string, SapArea>();
        }

        #endregion

        #region Main Audit Method

        /// <summary>
        /// Chạy kiểm toán cho một hoặc nhiều Load Pattern.
        /// </summary>
        /// <param name="loadPatterns">Danh sách pattern cách nhau bằng dấu phẩy</param>
        /// <returns>Danh sách báo cáo theo từng pattern</returns>
        public List<AuditReport> RunAudit(string loadPatterns)
        {
            var reports = new List<AuditReport>();

            if (string.IsNullOrEmpty(loadPatterns))
                return reports;

            // Parse patterns
            var patterns = loadPatterns.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                       .Select(p => p.Trim().ToUpper())
                                       .Distinct()
                                       .ToList();

            foreach (var pattern in patterns)
            {
                var report = RunSingleAudit(pattern);
                if (report != null)
                    reports.Add(report);
            }

            return reports;
        }

        /// <summary>
        /// Chạy kiểm toán cho một Load Pattern
        /// </summary>
        public AuditReport RunSingleAudit(string loadPattern)
        {
            var report = new AuditReport
            {
                LoadPattern = loadPattern,
                AuditDate = DateTime.Now,
                ModelName = SapUtils.GetModelName(),
                UnitInfo = UnitManager.Info.ToString()
            };

            // 1. Cache geometry
            CacheGeometry();

            // 2. Thu thập tất cả các loại tải
            var allLoads = new List<RawSapLoad>();
            allLoads.AddRange(SapUtils.GetAllFrameDistributedLoads(loadPattern));
            allLoads.AddRange(SapUtils.GetAllFramePointLoads(loadPattern));
            allLoads.AddRange(SapUtils.GetAllAreaUniformLoads(loadPattern));
            allLoads.AddRange(SapUtils.GetAllAreaUniformToFrameLoads(loadPattern));
            allLoads.AddRange(SapUtils.GetAllPointLoads(loadPattern));

            if (allLoads.Count == 0)
            {
                return report; // Không có tải -> báo cáo rỗng
            }

            // 3. Xác định danh sách tầng (dựa trên Z của phần tử)
            var storyElevations = DetermineStoryElevations(allLoads);

            // 4. Nhóm theo t?ng
            foreach (var storyInfo in storyElevations.OrderByDescending(s => s.Value))
            {
                var storyLoads = allLoads.Where(l =>
                    Math.Abs(l.ElementZ - storyInfo.Value) <= STORY_TOLERANCE).ToList();

                if (storyLoads.Count == 0) continue;

                var storyGroup = ProcessStory(storyInfo.Key, storyInfo.Value, storyLoads);
                if (storyGroup.LoadTypeGroups.Count > 0)
                    report.Stories.Add(storyGroup);
            }

            // 5. Lấy phản lực đáy để đối chiếu
            report.SapBaseReaction = SapUtils.GetBaseReactionZ(loadPattern);

            return report;
        }

        #endregion

        #region Processing Methods

        /// <summary>
        /// Xử lý một tầng
        /// </summary>
        private AuditStoryGroup ProcessStory(string storyName, double elevation, List<RawSapLoad> loads)
        {
            var storyGroup = new AuditStoryGroup
            {
                StoryName = storyName,
                Elevation = elevation
            };

            // Nhóm theo lo?i t?i
            var loadTypeGroups = loads.GroupBy(l => l.LoadType);

            foreach (var typeGroup in loadTypeGroups)
            {
                var typeResult = ProcessLoadType(typeGroup.Key, typeGroup.ToList());
                if (typeResult.ValueGroups.Count > 0)
                    storyGroup.LoadTypeGroups.Add(typeResult);
            }

            return storyGroup;
        }

        /// <summary>
        /// Xử lý một loại tải (Frame/Area/Point)
        /// </summary>
        private AuditLoadTypeGroup ProcessLoadType(string loadType, List<RawSapLoad> loads)
        {
            var typeGroup = new AuditLoadTypeGroup
            {
                LoadTypeName = GetLoadTypeDisplayName(loadType)
            };

            // Nhóm theo giá tr? t?i (v?i dung sai)
            var valueGroups = GroupByValue(loads);

            foreach (var valGroup in valueGroups.OrderByDescending(g => g.Key))
            {
                var valueResult = ProcessValueGroup(loadType, valGroup.Key, valGroup.ToList());
                if (valueResult.Entries.Count > 0)
                    typeGroup.ValueGroups.Add(valueResult);
            }

            return typeGroup;
        }

        /// <summary>
        /// Xử lý nhóm cùng giá trị tải
        /// </summary>
        private AuditValueGroup ProcessValueGroup(string loadType, double loadValue, List<RawSapLoad> loads)
        {
            var valueGroup = new AuditValueGroup
            {
                LoadValue = loadValue,
                Direction = loads.FirstOrDefault()?.Direction ?? "Gravity"
            };

            switch (loadType)
            {
                case "AreaUniform":
                case "AreaUniformToFrame":
                    ProcessAreaLoads(loads, valueGroup);
                    break;

                case "FrameDistributed":
                    ProcessFrameDistributedLoads(loads, valueGroup);
                    break;

                case "FramePoint":
                case "PointForce":
                    ProcessPointLoads(loads, valueGroup);
                    break;

                default:
                    ProcessGenericLoads(loads, valueGroup);
                    break;
            }

            return valueGroup;
        }

        /// <summary>
        /// Xử lý tải Area - Union geometry và tính diện tích
        /// </summary>
        private void ProcessAreaLoads(List<RawSapLoad> loads, AuditValueGroup valueGroup)
        {
            // Nhóm theo vị trí trục
            var gridGroups = loads.GroupBy(l => GetGridLocation(l.ElementName, "Area"));

            foreach (var gridGroup in gridGroups.OrderBy(g => g.Key))
            {
                var elemNames = gridGroup.Select(l => l.ElementName).ToList();
                var polygons = new List<Polygon>();

                foreach (var elemName in elemNames)
                {
                    if (_areaGeometryCache.TryGetValue(elemName, out var area))
                    {
                        var poly = CreateNtsPolygon(area.BoundaryPoints);
                        if (poly != null && poly.IsValid)
                            polygons.Add(poly);
                    }
                }

                if (polygons.Count == 0) continue;

                // Union để loại bỏ overlap
                Geometry unioned;
                try
                {
                    unioned = UnaryUnionOp.Union(polygons.Cast<Geometry>().ToList());
                }
                catch
                {
                    // Fallback n?u union fail
                    unioned = polygons.First();
                }

                // Tính diện tích và tạo diễn giải
                double totalAreaMm2 = unioned.Area;
                double totalAreaM2 = totalAreaMm2 * MM2_TO_M2;
                double force = totalAreaM2 * valueGroup.LoadValue;

                string explanation = FormatAreaExplanation(unioned, polygons.Count);

                valueGroup.Entries.Add(new AuditEntry
                {
                    GridLocation = gridGroup.Key,
                    Explanation = explanation,
                    Quantity = totalAreaM2,
                    Force = force,
                    ElementList = elemNames
                });
            }
        }

        /// <summary>
        /// Xử lý tải Frame phân bố - Tính tổng chiều dài
        /// </summary>
        private void ProcessFrameDistributedLoads(List<RawSapLoad> loads, AuditValueGroup valueGroup)
        {
            // Nhóm theo vị trí trục
            var gridGroups = loads.GroupBy(l => GetGridLocation(l.ElementName, "Frame"));

            foreach (var gridGroup in gridGroups.OrderBy(g => g.Key))
            {
                var elemNames = gridGroup.Select(l => l.ElementName).ToList();
                var lengths = new List<double>();

                foreach (var elemName in elemNames)
                {
                    if (_frameGeometryCache.TryGetValue(elemName, out var frame))
                    {
                        lengths.Add(frame.Length2D);
                    }
                }

                if (lengths.Count == 0) continue;

                double totalLengthMm = lengths.Sum();
                double totalLengthM = totalLengthMm * MM_TO_M;
                double force = totalLengthM * valueGroup.LoadValue;

                // Tạo diễn giải chiều dài
                string explanation = FormatLengthExplanation(lengths);

                valueGroup.Entries.Add(new AuditEntry
                {
                    GridLocation = gridGroup.Key,
                    Explanation = explanation,
                    Quantity = totalLengthM,
                    Force = force,
                    ElementList = elemNames
                });
            }
        }

        /// <summary>
        /// Xử lý tải tập trung
        /// </summary>
        private void ProcessPointLoads(List<RawSapLoad> loads, AuditValueGroup valueGroup)
        {
            // Nhóm theo vị trí trục
            var gridGroups = loads.GroupBy(l => GetGridLocation(l.ElementName, "Point"));

            foreach (var gridGroup in gridGroups.OrderBy(g => g.Key))
            {
                var elemNames = gridGroup.Select(l => l.ElementName).ToList();
                double totalForce = gridGroup.Sum(l => l.Value1);
                int count = gridGroup.Count();

                string explanation = count == 1
                    ? $"P = {totalForce:0.00} kN"
                    : $"{count} điểm × avg = {totalForce:0.00} kN";

                valueGroup.Entries.Add(new AuditEntry
                {
                    GridLocation = gridGroup.Key,
                    Explanation = explanation,
                    Quantity = count,
                    Force = totalForce,
                    ElementList = elemNames
                });
            }
        }

        /// <summary>
        /// Xử lý tải generic (fallback)
        /// </summary>
        private void ProcessGenericLoads(List<RawSapLoad> loads, AuditValueGroup valueGroup)
        {
            var gridGroups = loads.GroupBy(l => GetGridLocation(l.ElementName, "Unknown"));

            foreach (var gridGroup in gridGroups)
            {
                var elemNames = gridGroup.Select(l => l.ElementName).ToList();
                double totalValue = gridGroup.Sum(l => l.Value1);

                valueGroup.Entries.Add(new AuditEntry
                {
                    GridLocation = gridGroup.Key,
                    Explanation = $"{gridGroup.Count()} phần tử",
                    Quantity = gridGroup.Count(),
                    Force = totalValue,
                    ElementList = elemNames
                });
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Cache geometry từ SAP
        /// </summary>
        private void CacheGeometry()
        {
            _frameGeometryCache.Clear();
            _areaGeometryCache.Clear();

            // Cache frames
            var frames = SapUtils.GetAllFramesGeometry();
            foreach (var f in frames)
            {
                _frameGeometryCache[f.Name] = f;
            }

            // Cache areas
            var areas = SapUtils.GetAllAreasGeometry();
            foreach (var a in areas)
            {
                _areaGeometryCache[a.Name] = a;
            }
        }

        /// <summary>
        /// Xác định danh sách tầng từ cao độ phần tử
        /// </summary>
        private Dictionary<string, double> DetermineStoryElevations(List<RawSapLoad> loads)
        {
            var result = new Dictionary<string, double>();

            // Lấy tất cả Z từ loads
            var allZ = loads.Select(l => l.ElementZ).Distinct().OrderByDescending(z => z).ToList();

            // Nhóm Z gần nhau thành một tầng
            var zGroups = new List<List<double>>();
            foreach (var z in allZ)
            {
                var existingGroup = zGroups.FirstOrDefault(g => Math.Abs(g.Average() - z) <= STORY_TOLERANCE);
                if (existingGroup != null)
                {
                    existingGroup.Add(z);
                }
                else
                {
                    zGroups.Add(new List<double> { z });
                }
            }

            // Map với story từ Grid nếu có
            var zStories = _stories.Where(s => s.IsElevation).OrderByDescending(s => s.Elevation).ToList();
            int storyIndex = 1;

            foreach (var group in zGroups.OrderByDescending(g => g.Average()))
            {
                double avgZ = group.Average();

                // Tìm story gần nhất
                var matchingStory = zStories.FirstOrDefault(s => Math.Abs(s.Elevation - avgZ) <= STORY_TOLERANCE);

                string storyName;
                if (matchingStory != null)
                {
                    storyName = matchingStory.Name;
                }
                else
                {
                    storyName = $"Z={avgZ / 1000.0:0.0}m";
                }

                if (!result.ContainsKey(storyName))
                {
                    result[storyName] = avgZ;
                }

                storyIndex++;
            }

            return result;
        }

        /// <summary>
        /// Nhóm loads theo giá tr? (v?i dung sai)
        /// </summary>
        private IEnumerable<IGrouping<double, RawSapLoad>> GroupByValue(List<RawSapLoad> loads)
        {
            // Round giá tr? ?? gom nhóm
            return loads.GroupBy(l => Math.Round(l.Value1, 2));
        }

        /// <summary>
        /// Xác định vị trí theo trục
        /// </summary>
        private string GetGridLocation(string elementName, string elementType)
        {
            Point2D center = Point2D.Origin;

            if (elementType == "Frame" && _frameGeometryCache.TryGetValue(elementName, out var frame))
            {
                center = frame.Midpoint;
            }
            else if (elementType == "Area" && _areaGeometryCache.TryGetValue(elementName, out var area))
            {
                center = area.Centroid;
            }
            else if (elementType == "Point")
            {
                var points = SapUtils.GetAllPoints();
                var pt = points.FirstOrDefault(p => p.Name == elementName);
                if (pt != null)
                {
                    center = new Point2D(pt.X, pt.Y);
                }
            }

            // Tìm trục gần nhất
            string xGrid = FindNearestGrid(center.X, "X");
            string yGrid = FindNearestGrid(center.Y, "Y");

            if (!string.IsNullOrEmpty(xGrid) && !string.IsNullOrEmpty(yGrid))
            {
                return $"Trục {xGrid} / {yGrid}";
            }
            else if (!string.IsNullOrEmpty(xGrid))
            {
                return $"Trục {xGrid}";
            }
            else if (!string.IsNullOrEmpty(yGrid))
            {
                return $"Trục {yGrid}";
            }

            return $"({center.X / 1000:0.0}, {center.Y / 1000:0.0})";
        }

        /// <summary>
        /// Tìm tr?c g?n nh?t
        /// </summary>
        private string FindNearestGrid(double coord, string axis)
        {
            var grids = _grids.Where(g =>
                g.Orientation.Equals(axis, StringComparison.OrdinalIgnoreCase))
                .OrderBy(g => Math.Abs(g.Coordinate - coord))
                .ToList();

            if (grids.Count == 0) return null;

            var nearest = grids.First();
            if (Math.Abs(nearest.Coordinate - coord) > 5000) // > 5m thì không match
                return null;

            return nearest.Name;
        }

        /// <summary>
        /// Tạo polygon NTS từ danh sách điểm
        /// </summary>
        private Polygon CreateNtsPolygon(List<Point2D> pts)
        {
            if (pts == null || pts.Count < 3) return null;

            try
            {
                var coords = new List<Coordinate>();
                foreach (var p in pts)
                {
                    coords.Add(new Coordinate(p.X, p.Y));
                }

                // Đóng polygon
                if (!pts[0].Equals(pts.Last()))
                {
                    coords.Add(new Coordinate(pts[0].X, pts[0].Y));
                }

                var ring = _geometryFactory.CreateLinearRing(coords.ToArray());
                return _geometryFactory.CreatePolygon(ring);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Tạo diễn giải diện tích
        /// </summary>
        private string FormatAreaExplanation(Geometry geom, int originalCount)
        {
            if (geom == null) return "N/A";

            // Kiểm tra nếu là rectangle
            if (geom is Polygon poly && IsApproximateRectangle(poly))
            {
                var env = poly.EnvelopeInternal;
                double w = env.Width * MM_TO_M;
                double h = env.Height * MM_TO_M;
                return $"{w:0.0} x {h:0.0}";
            }

            // Polygon phức tạp
            double areaM2 = geom.Area * MM2_TO_M2;
            if (originalCount > 1)
            {
                return $"Union({originalCount}) = {areaM2:0.00}m²";
            }

            return $"Poly = {areaM2:0.00}m²";
        }

        /// <summary>
        /// Tạo diễn giải chiều dài
        /// </summary>
        private string FormatLengthExplanation(List<double> lengths)
        {
            if (lengths.Count == 0) return "N/A";

            if (lengths.Count == 1)
            {
                return $"L = {lengths[0] * MM_TO_M:0.0}m";
            }

            // Hi?n th? t?i ?a 5 chi?u dài
            var display = lengths.Take(5).Select(l => $"{l * MM_TO_M:0.0}").ToList();
            string result = string.Join(" + ", display);

            if (lengths.Count > 5)
            {
                result += $" +...({lengths.Count - 5})";
            }

            return result;
        }

        /// <summary>
        /// Kiểm tra polygon có gần như hình chữ nhật không
        /// </summary>
        private bool IsApproximateRectangle(Polygon poly)
        {
            if (poly.NumPoints != 5) return false; // 4 đỉnh + 1 điểm đóng

            var env = poly.EnvelopeInternal;
            double envArea = env.Area;
            double polyArea = poly.Area;

            // Nếu diện tích gần bằng envelope -> là HCN
            return Math.Abs(envArea - polyArea) / envArea < 0.05; // 5% tolerance
        }

        /// <summary>
        /// Lấy tên hiển thị cho loại tải
        /// </summary>
        private string GetLoadTypeDisplayName(string loadType)
        {
            switch (loadType)
            {
                case "AreaUniform": return "SÀN - UNIFORM LOAD (kN/m²)";
                case "AreaUniformToFrame": return "SÀN - UNIFORM TO FRAME (kN/m²)";
                case "FrameDistributed": return "DẦM/TƯỜNG - DISTRIBUTED (kN/m)";
                case "FramePoint": return "DẦM - POINT LOAD (kN)";
                case "PointForce": return "ĐIỂM - POINT FORCE (kN)";
                case "JointMass": return "KHỐI LƯỢNG - JOINT MASS";
                default: return loadType.ToUpper();
            }
        }

        #endregion

        #region Report Generation

        /// <summary>
        /// Tạo báo cáo dạng text
        /// </summary>
        public string GenerateTextReport(AuditReport report)
        {
            var sb = new StringBuilder();

            sb.AppendLine("===================================================================");
            sb.AppendLine("   BÁO CÁO KIỂM TOÁN TẢI TRỌNG - DTS ENGINE");
            sb.AppendLine($"   Ngày: {report.AuditDate:dd/MM/yyyy HH:mm}");
            sb.AppendLine($"   Model: {report.ModelName}");
            sb.AppendLine($"   Load Case: {report.LoadPattern}");
            sb.AppendLine($"   Đơn vị: {report.UnitInfo}");
            sb.AppendLine("===================================================================");
            sb.AppendLine();

            foreach (var story in report.Stories)
            {
                sb.AppendLine($"--- TẦNG: {story.StoryName} (Z = {story.Elevation / 1000.0:0.0}m) ---");
                sb.AppendLine();

                foreach (var loadType in story.LoadTypeGroups)
                {
                    sb.AppendLine($"[{loadType.LoadTypeName}]");
                    sb.AppendLine();

                    foreach (var valGroup in loadType.ValueGroups)
                    {
                        sb.AppendLine($"    > Nhóm giá trị: {valGroup.LoadValue:0.00} ({valGroup.Direction})");
                        sb.AppendLine(new string('-', 95));
                        sb.AppendLine(string.Format("    | {0,-22} | {1,-32} | {2,10} | {3,12} |",
                            "Vị trí (Trục)", "Diễn giải", "SL/DT", "Lực (kN)"));
                        sb.AppendLine(new string('-', 95));

                        foreach (var entry in valGroup.Entries)
                        {
                            string loc = entry.GridLocation.Length > 22
                                ? entry.GridLocation.Substring(0, 19) + "..."
                                : entry.GridLocation;

                            string exp = entry.Explanation.Length > 32
                                ? entry.Explanation.Substring(0, 29) + "..."
                                : entry.Explanation;

                            sb.AppendLine(string.Format("    | {0,-22} | {1,-32} | {2,10:0.00} | {3,12:0.00} |",
                                loc, exp, entry.Quantity, entry.Force));
                        }

                        sb.AppendLine(new string('-', 95));
                        sb.AppendLine(string.Format("    | {0,-57} | {1,10:0.00} | {2,12:0.00} |",
                            $"TỔNG NHÓM {valGroup.LoadValue:0.00}",
                            valGroup.TotalQuantity, valGroup.TotalForce));
                        sb.AppendLine(new string('-', 95));
                        sb.AppendLine();
                    }
                }

                sb.AppendLine($">>> TỔNG TẦNG {story.StoryName}: {story.SubTotalForce:n2} kN");
                sb.AppendLine();
            }

            sb.AppendLine("===================================================================");
            sb.AppendLine($"TỔNG CỘNG TÍNH TOÁN: {report.TotalCalculatedForce:n2} kN");

            if (Math.Abs(report.SapBaseReaction) > 0.01)
            {
                sb.AppendLine($"SAP2000 BASE REACTION (Z): {report.SapBaseReaction:n2} kN");
                sb.AppendLine($"SAI LỆCH: {report.Difference:n2} kN ({report.DifferencePercent:0.00}%)");

                if (Math.Abs(report.DifferencePercent) < 1.0)
                {
                    sb.AppendLine(">>> KIỂM TRA: OK (sai lệch < 1%)");
                }
                else if (Math.Abs(report.DifferencePercent) < 5.0)
                {
                    sb.AppendLine(">>> KIỂM TRA: CHẤP NHẬN (sai lệch < 5%)");
                }
                else
                {
                    sb.AppendLine(">>> KIỂM TRA: CẦN XEM XÉT (sai lệch > 5%)");
                }
            }
            else
            {
                sb.AppendLine("SAP2000 BASE REACTION: Chưa có (model chưa chạy phân tích)");
            }

            sb.AppendLine("===================================================================");

            return sb.ToString();
        }

        /// <summary>
        /// T?o báo cáo chi ti?t bao g?m danh sách ph?n t?
        /// </summary>
        public string GenerateDetailedReport(AuditReport report)
        {
            var sb = new StringBuilder();

            sb.AppendLine(GenerateTextReport(report));
            sb.AppendLine();
            sb.AppendLine("=== CHI TIẾT PHẦN TỬ ===");
            sb.AppendLine();

            foreach (var story in report.Stories)
            {
                sb.AppendLine($"--- {story.StoryName} ---");

                foreach (var loadType in story.LoadTypeGroups)
                {
                    foreach (var valGroup in loadType.ValueGroups)
                    {
                        foreach (var entry in valGroup.Entries)
                        {
                            if (entry.ElementList.Count > 0)
                            {
                                sb.AppendLine($"  {entry.GridLocation}:");
                                sb.AppendLine($"    Phần tử: {string.Join(", ", entry.ElementList.Take(20))}");
                                if (entry.ElementList.Count > 20)
                                {
                                    sb.AppendLine($"    ... và {entry.ElementList.Count - 20} phần tử khác");
                                }
                            }
                        }
                    }
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        #endregion
    }
}
