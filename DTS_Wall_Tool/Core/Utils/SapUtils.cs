using DTS_Wall_Tool.Core.Data;
using DTS_Wall_Tool.Core.Primitives;
using SAP2000v1;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace DTS_Wall_Tool.Core.Utils
{
    /// <summary>
    /// Tiện ích kết nối và làm việc với SAP2000
    /// Hỗ trợ đồng bộ 2 chiều: Đọc/Ghi tải trọng
    /// </summary>
    public static class SapUtils
    {
        #region Connection

        private static cOAPI _sapObject = null;
        private static cSapModel _sapModel = null;

        /// <summary>
        /// Kết nối đến SAP2000 đang chạy (Sử dụng Helper chuẩn)
        /// 
        /// ⚠️ QUAN TRỌNG - KHÔNG SỬA HÀM NÀY:
        /// - PHẢI dùng cHelper.GetObject() - Cách DUY NHẤT ổn định cho SAP v26+
        /// - KHÔNG dùng Marshal.GetActiveObject() - Sẽ KHÔNG hoạt động với v26
        /// - KHÔNG thay đổi chuỗi "CSI.SAP2000.API.SapObject" (không có dấu cách)
        /// - Đơn vị chuẩn: eUnits.kN_mm_C (enum = 5) để đồng bộ với CAD
        /// </summary>
        public static bool Connect(out string message)
        {
            _sapObject = null;
            _sapModel = null;
            message = "";

            try
            {
                // 1. Dùng Helper - Cách duy nhất ổn định cho SAP v26+
                cHelper myHelper = new Helper();

                // 2. Lấy object đang chạy (KHÔNG có dấu cách trong chuỗi)
                _sapObject = myHelper.GetObject("CSI.SAP2000.API.SapObject");

                if (_sapObject != null)
                {
                    // 3. Lấy Model
                    _sapModel = _sapObject.SapModel;

                    // 4. Thiết lập đơn vị chuẩn (kN, mm, C) để đồng bộ với CAD
                    _sapModel.SetPresentUnits(eUnits.kN_mm_C);

                    // Lấy tên file để confirm
                    string modelName = "Unknown";
                    try { modelName = System.IO.Path.GetFileName(_sapModel.GetModelFilename()); } catch { }

                    message = $"Kết nối thành công: {modelName}";
                    return true;
                }
                else
                {
                    message = "Không tìm thấy SAP2000 đang chạy. Hãy mở SAP2000 trước.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                message = $"Lỗi kết nối SAP: {ex.Message}";

                // Gợi ý fix lỗi COM phổ biến
                if (ex.Message.Contains("cast") || ex.Message.Contains("COM"))
                {
                    message += "\n(Gợi ý: Chạy RegisterSAP2000.exe trong thư mục cài đặt SAP bằng quyền Admin)";
                }

                return false;
            }
        }

        public static void Disconnect()
        {
            _sapModel = null;
            _sapObject = null;
        }

        public static bool IsConnected => _sapModel != null;

        public static cSapModel GetModel()
        {
            if (_sapModel == null) Connect(out _);
            return _sapModel;
        }

        #endregion

        #region Frame Geometry

        public static int CountFrames()
        {
            var model = GetModel();
            if (model == null) return -1;

            int count = 0;
            string[] names = null;
            int ret = model.FrameObj.GetNameList(ref count, ref names);
            return (ret == 0) ? count : 0;
        }

        public static List<SapFrame> GetAllFramesGeometry()
        {
            var listFrames = new List<SapFrame>();
            var model = GetModel();
            if (model == null) return listFrames;

            int count = 0;
            string[] frameNames = null;
            model.FrameObj.GetNameList(ref count, ref frameNames);

            if (count == 0 || frameNames == null) return listFrames;

            foreach (var name in frameNames)
            {
                var frame = GetFrameGeometry(name);
                if (frame != null) listFrames.Add(frame);
            }

            return listFrames;
        }

        public static SapFrame GetFrameGeometry(string frameName)
        {
            var model = GetModel();
            if (model == null) return null;

            string p1Name = "", p2Name = "";
            int ret = model.FrameObj.GetPoints(frameName, ref p1Name, ref p2Name);
            if (ret !=0) return null;

            double x1 =0, y1 =0, z1 =0;
            ret = model.PointObj.GetCoordCartesian(p1Name, ref x1, ref y1, ref z1, "Global");
            if (ret !=0) return null;

            double x2 =0, y2 =0, z2 =0;
            ret = model.PointObj.GetCoordCartesian(p2Name, ref x2, ref y2, ref z2, "Global");
            if (ret !=0) return null;

            return new SapFrame
            {
                Name = frameName,
                StartPt = new Point2D(x1, y1),
                EndPt = new Point2D(x2, y2),
                Z1 = z1,
                Z2 = z2
            };
        }

        public static List<SapFrame> GetBeamsAtElevation(double elevation, double tolerance = 200)
        {
            var result = new List<SapFrame>();
            var allFrames = GetAllFramesGeometry();

            foreach (var f in allFrames)
            {
                if (f.IsVertical) continue;

                double avgZ = (f.Z1 + f.Z2) / 2.0;
                if (Math.Abs(avgZ - elevation) <= tolerance)
                {
                    result.Add(f);
                }
            }
            return result;
        }

        #endregion

        #region Load Reading - ĐỌC TẢI TRỌNG TỪ SAP

        /// <summary>
        /// Đọc tất cả tải phân bố trên một frame
        /// </summary>
        public static List<SapLoadInfo> GetFrameDistributedLoads(string frameName, string loadPattern = null)
        {
            var loads = new List<SapLoadInfo>();
            var model = GetModel();
            if (model == null) return loads;

            try
            {
                int numberItems = 0;
                string[] frameNames = null;
                string[] loadPatterns = null;
                int[] myTypes = null;
                string[] csys = null;
                int[] dirs = null;
                double[] rd1 = null, rd2 = null;
                double[] dist1 = null, dist2 = null;
                double[] val1 = null, val2 = null;

                int ret = model.FrameObj.GetLoadDistributed(
                    frameName,
                    ref numberItems,
                    ref frameNames,
                    ref loadPatterns,
                    ref myTypes,
                    ref csys,
                    ref dirs,
                    ref rd1, ref rd2,
                    ref dist1, ref dist2,
                    ref val1, ref val2,
                    eItemType.Objects
                );

                if (ret != 0 || numberItems == 0) return loads;

                for (int i = 0; i < numberItems; i++)
                {
                    // Lọc theo pattern nếu có
                    if (!string.IsNullOrEmpty(loadPattern) &&
                        !loadPatterns[i].Equals(loadPattern, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var loadInfo = new SapLoadInfo
                    {
                        FrameName = frameNames[i],
                        LoadPattern = loadPatterns[i],
                        LoadValue = val1[i] * 1000.0, // kN/mm -> kN/m
                        DistanceI = dist1[i],
                        DistanceJ = dist2[i],
                        Direction = GetDirectionName(dirs[i]),
                        LoadType = "Distributed"
                    };

                    loads.Add(loadInfo);
                }
            }
            catch { }

            return loads;
        }

        /// <summary>
        /// Đọc tổng tải trọng trên frame theo pattern
        /// </summary>
        public static double GetFrameTotalLoad(string frameName, string loadPattern)
        {
            var loads = GetFrameDistributedLoads(frameName, loadPattern);
            if (loads.Count == 0) return 0;

            // Tính tổng tải (có thể có nhiều đoạn)
            double total = 0;
            foreach (var load in loads)
            {
                total += load.LoadValue;
            }
            return total;
        }

        /// <summary>
        /// Kiểm tra frame có tải trọng không
        /// </summary>
        public static bool FrameHasLoad(string frameName, string loadPattern)
        {
            var loads = GetFrameDistributedLoads(frameName, loadPattern);
            return loads.Count > 0;
        }

        /// <summary>
        /// Lấy danh sách frame đã có tải theo pattern
        /// </summary>
        public static Dictionary<string, List<SapLoadInfo>> GetAllFrameLoads(string loadPattern)
        {
            var result = new Dictionary<string, List<SapLoadInfo>>();
            var model = GetModel();
            if (model == null) return result;

            // Lấy tất cả frame names
            int count = 0;
            string[] frameNames = null;
            model.FrameObj.GetNameList(ref count, ref frameNames);

            if (frameNames == null) return result;

            foreach (var frameName in frameNames)
            {
                var loads = GetFrameDistributedLoads(frameName, loadPattern);
                if (loads.Count > 0)
                {
                    result[frameName] = loads;
                }
            }

            return result;
        }

        private static string GetDirectionName(int dir)
        {
            switch (dir)
            {
                case 1: return "Local 1";
                case 2: return "Local 2";
                case 3: return "Local 3";
                case 4: return "X";
                case 5: return "Y";
                case 6: return "Z";
                case 7: return "X Projected";
                case 8: return "Y Projected";
                case 9: return "Z Projected";
                case 10: return "Gravity";
                case 11: return "Gravity Projected";
                default: return "Unknown";
            }
        }

        #endregion

        #region Load Writing - GHI TẢI TRỌNG VÀO SAP

        /// <summary>
        /// Gán tải phân bố lên frame
        /// </summary>
        public static bool AssignDistributedLoad(string frameName, string loadPattern,
            double loadValue, double distI = 0, double distJ = 0, bool isRelative = false)
        {
            var model = GetModel();
            if (model == null) return false;

            try
            {
                // Đổi đơn vị: kN/m -> kN/mm
                double val_kN_mm = loadValue / 1000.0;

                int ret = model.FrameObj.SetLoadDistributed(
                    frameName,
                    loadPattern,
                    1,          // Force/Length
                    10,         // Gravity Direction
                    distI, distJ,
                    val_kN_mm, val_kN_mm,
                    "Global",
                    isRelative,
                    true,       // Replace existing
                    eItemType.Objects
                );

                return ret == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gán tải từ MappingRecord
        /// </summary>
        public static bool AssignLoadFromMapping(MappingRecord mapping, string loadPattern, double loadValue)
        {
            if (string.IsNullOrEmpty(mapping.TargetFrame) || mapping.TargetFrame == "New")
                return false;

            return AssignDistributedLoad(
                mapping.TargetFrame,
                loadPattern,
                loadValue,
                mapping.DistI,
                mapping.DistJ,
                false
            );
        }

        /// <summary>
        /// Xóa tất cả tải trên frame theo pattern
        /// </summary>
        public static bool DeleteFrameLoads(string frameName, string loadPattern)
        {
            var model = GetModel();
            if (model == null) return false;

            try
            {
                int ret = model.FrameObj.DeleteLoadDistributed(frameName, loadPattern);
                return ret == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Xóa và gán lại tải (đảm bảo clean)
        /// </summary>
        public static bool ReplaceFrameLoad(string frameName, string loadPattern,
            double loadValue, double distI = 0, double distJ = 0)
        {
            // Xóa tải cũ
            DeleteFrameLoads(frameName, loadPattern);

            // Gán tải mới
            return AssignDistributedLoad(frameName, loadPattern, loadValue, distI, distJ, false);
        }

        #endregion

        #region Frame Change Detection - PHÁT HIỆN THAY ĐỔI

        /// <summary>
        /// Tạo hash từ geometry frame để phát hiện thay đổi
        /// </summary>
        public static string GetFrameGeometryHash(string frameName)
        {
            var frame = GetFrameGeometry(frameName);
            if (frame == null) return null;

            // Hash = "X1,Y1,Z1|X2,Y2,Z2"
            string data = $"{frame.StartPt.X:0.0},{frame.StartPt.Y:0.0},{frame.Z1:0.0}|" +
                          $"{frame.EndPt.X:0.0},{frame.EndPt.Y:0.0},{frame.Z2:0.0}";

            return ComputeHash(data);
        }

        /// <summary>
        /// Tạo hash từ tải trọng frame
        /// </summary>
        public static string GetFrameLoadHash(string frameName, string loadPattern)
        {
            var loads = GetFrameDistributedLoads(frameName, loadPattern);
            if (loads.Count == 0) return "NOLOAD";

            var sortedLoads = loads.OrderBy(l => l.DistanceI).ToList();
            string data = string.Join("|", sortedLoads.Select(l =>
                $"{l.LoadValue:0.00},{l.DistanceI:0},{l.DistanceJ:0}"));

            return ComputeHash(data);
        }

        /// <summary>
        /// Kiểm tra frame có tồn tại trong SAP không
        /// </summary>
        public static bool FrameExists(string frameName)
        {
            var model = GetModel();
            if (model == null) return false;

            int count = 0;
            string[] names = null;
            model.FrameObj.GetNameList(ref count, ref names);

            if (names == null) return false;
            return names.Contains(frameName);
        }

        /// <summary>
        /// Tìm frame mới được tạo/merge gần vị trí cũ
        /// </summary>
        public static string FindReplacementFrame(Point2D oldStart, Point2D oldEnd, double elevation, double tolerance = 500)
        {
            var beams = GetBeamsAtElevation(elevation, 200);

            foreach (var beam in beams)
            {
                // Kiểm tra overlap với vị trí cũ
                double dist1 = Math.Min(
                    oldStart.DistanceTo(beam.StartPt),
                    oldStart.DistanceTo(beam.EndPt)
                );
                double dist2 = Math.Min(
                    oldEnd.DistanceTo(beam.StartPt),
                    oldEnd.DistanceTo(beam.EndPt)
                );

                if (dist1 < tolerance || dist2 < tolerance)
                {
                    return beam.Name;
                }
            }

            return null;
        }

        private static string ComputeHash(string input)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                return BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 8);
            }
        }

        #endregion

        #region Load Patterns & Stories

        public static bool LoadPatternExists(string patternName)
        {
            var model = GetModel();
            if (model == null) return false;

            int count = 0;
            string[] names = null;
            model.LoadPatterns.GetNameList(ref count, ref names);

            if (names == null) return false;
            foreach (var n in names)
            {
                if (n.Equals(patternName, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static List<string> GetLoadPatterns()
        {
            var patterns = new List<string>();
            var model = GetModel();
            if (model == null) return patterns;

            int count = 0;
            string[] names = null;
            model.LoadPatterns.GetNameList(ref count, ref names);
            if (names != null) patterns.AddRange(names);
            return patterns;
        }

        /// <summary>
        /// Read grid lines from SAP2000 DatabaseTables (table key: "Grid Lines")
        /// Returns simple records with Name, Orientation and Coordinate
        /// </summary>
        public class GridLineRecord
        {
            public string Name { get; set; }
            public string Orientation { get; set; } // e.g., "X" or "Y"
            public double Coordinate { get; set; }
            public override string ToString() => $"{Name}: {Orientation}={Coordinate}";
        }

        public static List<GridLineRecord> GetGridLines()
        {
            var result = new List<GridLineRecord>();
            var model = GetModel();
            if (model == null) return result;

            string[] candidateTableKeys = new[] { "Grid Lines" };

            foreach (var tableKey in candidateTableKeys)
            {
                try
                {
                    int tableVersion = 0;
                    string[] fieldNames = null;
                    string[] tableData = null;
                    int numberRecords = 0;
                    string[] fieldsKeysIncluded = null;

                    // Use FieldKeyListInput = [""] to request all fields (matches VBA GetTableForDisplayArray usage)
                    string[] fieldKeyListInput = new string[] { "" };
                    int ret = model.DatabaseTables.GetTableForDisplayArray(
                        tableKey,
                        ref fieldKeyListInput, "All", ref tableVersion, ref fieldsKeysIncluded,
                        ref numberRecords, ref tableData
                    );

                    if (ret != 0 || tableData == null || fieldsKeysIncluded == null || numberRecords == 0)
                        continue;

                    int colCount = fieldsKeysIncluded.Length;
                    if (colCount == 0) continue;

                    // Determine indices from field keys
                    int axisDirIdx = Array.IndexOf(fieldsKeysIncluded, "AxisDir");
                    if (axisDirIdx < 0) axisDirIdx = Array.FindIndex(fieldsKeysIncluded, f => f != null && f.ToLowerInvariant().Contains("axis"));

                    int gridIdIdx = Array.IndexOf(fieldsKeysIncluded, "GridID");
                    if (gridIdIdx < 0) gridIdIdx = Array.FindIndex(fieldsKeysIncluded, f => f != null && (f.ToLowerInvariant().Contains("gridid") || f.ToLowerInvariant().Contains("grid")));

                    int coordIdx = Array.IndexOf(fieldsKeysIncluded, "XRYZCoord");
                    if (coordIdx < 0) coordIdx = Array.FindIndex(fieldsKeysIncluded, f => f != null && f.ToLowerInvariant().Contains("coord"));

                    for (int r = 0; r < numberRecords; r++)
                    {
                        try
                        {
                            string axisDir = axisDirIdx >= 0 ? tableData[r * colCount + axisDirIdx]?.Trim() : null;
                            string gridId = gridIdIdx >= 0 ? tableData[r * colCount + gridIdIdx]?.Trim() : null;
                            string coordStr = coordIdx >= 0 ? tableData[r * colCount + coordIdx]?.Trim() : null;

                            double coord = 0;
                            if (!string.IsNullOrEmpty(coordStr))
                            {
                                // XRYZCoord may be like "0" or "5000"; take numeric value
                                var parts = coordStr.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length > 0)
                                {
                                    var tryS = parts[0];
                                    if (!double.TryParse(tryS, NumberStyles.Any, CultureInfo.InvariantCulture, out coord))
                                    {
                                        double.TryParse(tryS, out coord);
                                    }
                                }
                            }

                            if (!string.IsNullOrEmpty(axisDir))
                                axisDir = axisDir.Split(new[] { ' ', '_' }, StringSplitOptions.RemoveEmptyEntries)[0];

                            result.Add(new GridLineRecord
                            {
                                Name = gridId ?? string.Empty,
                                Orientation = axisDir ?? string.Empty,
                                Coordinate = coord
                            });
                        }
                        catch { }
                    }

                    if (result.Count > 0) return result;
                }
                catch { }
            }

            return result;
        }

        public static void RefreshView()
        {
            GetModel()?.View.RefreshView();
        }

        #endregion

        public class GridStoryItem
        {
            public string AxisDir { get; set; } // X/Y/Z
            public string Name { get; set; }
            public double Coordinate { get; set; }
            public bool IsElevation => !string.IsNullOrEmpty(AxisDir) && AxisDir.Trim().StartsWith("Z", StringComparison.OrdinalIgnoreCase);
            // Backward-compatibility with earlier StoryData callers
            public string StoryName
            {
                get => Name;
                set => Name = value;
            }

            public double Elevation
            {
                get => Coordinate;
                set => Coordinate = value;
            }

            public DTS_Wall_Tool.Core.Data.StoryData ToStoryData()
            {
                return new DTS_Wall_Tool.Core.Data.StoryData
                {
                    StoryName = this.StoryName,
                    Elevation = this.Elevation,
                    StoryHeight =3300
                };
            }
            public override string ToString() => $"{AxisDir}\t{Name}\t{Coordinate}";
        }

        /// <summary>
        /// Unified reader: read Grid Lines and return all grid/story entries as GridStoryItem.
        /// Use AxisDir to distinguish X/Y (grid axes) and Z (story elevations).
        /// </summary>
        public static List<GridStoryItem> GetStories()
        {
            var result = new List<GridStoryItem>();
            try
            {
                var grids = GetGridLines();
                foreach (var g in grids)
                {
                    result.Add(new GridStoryItem
                    {
                        AxisDir = g.Orientation ?? string.Empty,
                        Name = g.Name ?? string.Empty,
                        Coordinate = g.Coordinate
                    });
                }

                // Sort: first by AxisDir (Z last or group by Z?), but keep original order; provide sorted views as needed by caller
                // For convenience, return sorted by AxisDir then coordinate
                result = result.OrderBy(r => r.AxisDir).ThenBy(r => r.Coordinate).ToList();
            }
            catch { }
            return result;
        }
    }
}