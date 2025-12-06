using DTS_Engine.Core.Data;
using SAP2000v1;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DTS_Engine.Core.Utils
{
    /// <summary>
    /// ROBUST SAP2000 DATABASE TABLE READER
    /// 
    /// Gi?i quy?t v?n ??:
    /// 1. Schema Detection - T? ??ng nh?n di?n c?t (không ph? thu?c th? t?)
    /// 2. Local Axes Support - Chuy?n ??i F1/F2/F3 sang Global X/Y/Z
    /// 3. Direction Resolver - Xác ??nh chi?u t?i th?c theo CoordSys
    /// 
    /// Nguyên t?c:
    /// - Không "?oán mò" tên c?t ? Dùng Fuzzy matching
    /// - Không gi? ??nh th? t? ? Dùng Dictionary lookup
    /// - H? tr? nhi?u version SAP ? Schema flexible
    /// </summary>
    public class SapDatabaseReader
    {
        private readonly cSapModel _model;
        private readonly Dictionary<string, TableSchema> _schemaCache;

        public SapDatabaseReader(cSapModel model)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _schemaCache = new Dictionary<string, TableSchema>();
        }

        #region Schema Detection

        /// <summary>
        /// Table Schema - Metadata c?a b?ng SAP
        /// </summary>
        public class TableSchema
        {
            public string TableName { get; set; }
            public Dictionary<string, int> ColumnMap { get; set; } // ColumnName ? Index
            public string[] FieldKeys { get; set; }
            public string[] TableData { get; set; }
            public int RecordCount { get; set; }
            public int ColumnCount { get; set; }

            public TableSchema()
            {
                ColumnMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }

            /// <summary>
            /// L?y giá tr? chu?i t?i (row, columnName)
            /// </summary>
            public string GetString(int row, string columnName)
            {
                if (row < 0 || row >= RecordCount) return null;
                if (!ColumnMap.TryGetValue(columnName, out int colIdx)) return null;
                int idx = row * ColumnCount + colIdx;
                if (idx < 0 || idx >= TableData.Length) return null;
                return TableData[idx];
            }

            /// <summary>
            /// L?y giá tr? double t?i (row, columnName)
            /// </summary>
            public double GetDouble(int row, string columnName)
            {
                string val = GetString(row, columnName);
                if (string.IsNullOrEmpty(val)) return 0.0;
                if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out double result))
                    return result;
                return 0.0;
            }

            /// <summary>
            /// Ki?m tra c?t có t?n t?i không
            /// </summary>
            public bool HasColumn(string columnName) => ColumnMap.ContainsKey(columnName);

            /// <summary>
            /// Tìm tên c?t theo pattern (Fuzzy matching)
            /// Ví d?: FindColumn("Load") ? "LoadPat" ho?c "OutputCase"
            /// </summary>
            public string FindColumn(params string[] patterns)
            {
                foreach (var pattern in patterns)
                {
                    foreach (var col in ColumnMap.Keys)
                    {
                        if (col.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                            return col;
                    }
                }
                return null;
            }
        }

        /// <summary>
        /// ??c schema c?a b?ng SAP2000
        /// Cache l?i ?? không ph?i ??c l?i nhi?u l?n
        /// </summary>
        public TableSchema GetTableSchema(string tableName, string patternFilter = null)
        {
            string cacheKey = $"{tableName}|{patternFilter ?? "ALL"}";
            if (_schemaCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var schema = new TableSchema { TableName = tableName };

            try
            {
                int tableVer = 0;
                string[] fields = null;
                int numRec = 0;
                string[] tableData = null;
                string[] input = new string[] { };

                int ret = _model.DatabaseTables.GetTableForDisplayArray(
                    tableName, ref input, "All", ref tableVer, ref fields, ref numRec, ref tableData);

                if (ret != 0 || numRec == 0 || fields == null || tableData == null)
                    return schema;

                schema.FieldKeys = fields;
                schema.TableData = tableData;
                schema.RecordCount = numRec;
                schema.ColumnCount = fields.Length;

                // Build column map
                for (int i = 0; i < fields.Length; i++)
                {
                    if (!string.IsNullOrEmpty(fields[i]))
                    {
                        schema.ColumnMap[fields[i].Trim()] = i;
                    }
                }

                _schemaCache[cacheKey] = schema;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetTableSchema failed for '{tableName}': {ex.Message}");
            }

            return schema;
        }

        #endregion

        #region Load Direction Resolver

        /// <summary>
        /// Resolved Load Direction - K?t qu? sau khi chuy?n ??i Local ? Global
        /// </summary>
        public class ResolvedDirection
        {
            public string GlobalAxis { get; set; } // "X", "Y", "Z"
            public double Sign { get; set; } // +1 ho?c -1
            public string Description { get; set; } // Mô t? cho debug

            public override string ToString() => $"{(Sign > 0 ? "+" : "")}{GlobalAxis} ({Description})";
        }

        /// <summary>
        /// Resolve h??ng t?i t? Direction string và CoordSys
        /// 
        /// Logic:
        /// 1. N?u CoordSys = GLOBAL ? Gravity/Z = -Z, X = +X, Y = +Y
        /// 2. N?u CoordSys = Local ? C?n ??c Local Axes c?a element
        /// 3. Direction code: 1=Local1, 2=Local2, 3=Local3 (ho?c Gravity)
        /// </summary>
        public ResolvedDirection ResolveDirection(string elementName, string elementType, string direction, string coordSys)
        {
            var result = new ResolvedDirection();

            // Case 1: GLOBAL CoordSys
            if (string.Equals(coordSys, "GLOBAL", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(direction, "Gravity", StringComparison.OrdinalIgnoreCase))
                {
                    result.GlobalAxis = "Z";
                    result.Sign = -1.0;
                    result.Description = "Global Gravity";
                }
                else if (direction == "1" || direction.Contains("X"))
                {
                    result.GlobalAxis = "X";
                    result.Sign = 1.0;
                    result.Description = "Global X";
                }
                else if (direction == "2" || direction.Contains("Y"))
                {
                    result.GlobalAxis = "Y";
                    result.Sign = 1.0;
                    result.Description = "Global Y";
                }
                else if (direction == "3" || direction.Contains("Z"))
                {
                    result.GlobalAxis = "Z";
                    result.Sign = 1.0;
                    result.Description = "Global Z";
                }
                else
                {
                    result.GlobalAxis = "Z";
                    result.Sign = -1.0;
                    result.Description = "Unknown (assume Gravity)";
                }

                return result;
            }

            // Case 2: LOCAL CoordSys ? C?n Local Axes
            // TODO: Implement full local axes transformation
            // Hi?n t?i: Gi? ??nh Local 3 = Gravity cho Area/Frame ??ng th?ng
            if (direction == "3" || string.Equals(direction, "Gravity", StringComparison.OrdinalIgnoreCase))
            {
                result.GlobalAxis = "Z";
                result.Sign = -1.0;
                result.Description = "Local 3 (assumed vertical)";
            }
            else if (direction == "1")
            {
                result.GlobalAxis = "X"; // Simplified assumption
                result.Sign = 1.0;
                result.Description = "Local 1 (approx X)";
            }
            else if (direction == "2")
            {
                result.GlobalAxis = "Y"; // Simplified assumption
                result.Sign = 1.0;
                result.Description = "Local 2 (approx Y)";
            }
            else
            {
                result.GlobalAxis = "Z";
                result.Sign = -1.0;
                result.Description = "Local unknown (assume Gravity)";
            }

            return result;
        }

        /// <summary>
        /// L?y Local Axes c?a Frame (c?n ?? chuy?n ??i chính xác)
        /// TODO: Implement full transformation matrix
        /// </summary>
        public LocalAxesInfo GetFrameLocalAxes(string frameName)
        {
            try
            {
                double ang = 0;
                bool advanced = false;
                int ret = _model.FrameObj.GetLocalAxes(frameName, ref ang, ref advanced);

                if (ret == 0)
                {
                    return new LocalAxesInfo
                    {
                        ElementName = frameName,
                        Angle = ang,
                        IsAdvanced = advanced
                    };
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// L?y Local Axes c?a Area
        /// </summary>
        public LocalAxesInfo GetAreaLocalAxes(string areaName)
        {
            try
            {
                double ang = 0;
                bool advanced = false;
                int ret = _model.AreaObj.GetLocalAxes(areaName, ref ang, ref advanced);

                if (ret == 0)
                {
                    return new LocalAxesInfo
                    {
                        ElementName = areaName,
                        Angle = ang,
                        IsAdvanced = advanced
                    };
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Local Axes Information
        /// </summary>
        public class LocalAxesInfo
        {
            public string ElementName { get; set; }
            public double Angle { get; set; } // Rotation angle (deg)
            public bool IsAdvanced { get; set; }
        }

        #endregion

        #region High-Level Load Readers

        /// <summary>
        /// ??c Frame Distributed Loads v?i Direction ?ã resolve
        /// </summary>
        public List<RawSapLoad> ReadFrameDistributedLoads(string patternFilter = null)
        {
            var loads = new List<RawSapLoad>();
            var schema = GetTableSchema("Frame Loads - Distributed", patternFilter);

            if (schema.RecordCount == 0) return loads;

            // Tìm c?t (Fuzzy matching ?? h? tr? nhi?u version SAP)
            string colFrame = schema.FindColumn("Frame");
            string colPattern = schema.FindColumn("LoadPat", "OutputCase");
            string colDir = schema.FindColumn("Dir");
            string colCoordSys = schema.FindColumn("CoordSys");
            string colFOverLA = schema.FindColumn("FOverLA", "FOverL");
            string colAbsDistA = schema.FindColumn("AbsDistA");
            string colAbsDistB = schema.FindColumn("AbsDistB");

            if (colFrame == null || colPattern == null) return loads;

            // Cache geometry
            var frameGeomMap = new Dictionary<string, double>();
            var frames = SapUtils.GetAllFramesGeometry();
            foreach (var f in frames) frameGeomMap[f.Name] = f.AverageZ;

            for (int r = 0; r < schema.RecordCount; r++)
            {
                string frameName = schema.GetString(r, colFrame);
                string pattern = schema.GetString(r, colPattern);

                if (string.IsNullOrEmpty(frameName) || string.IsNullOrEmpty(pattern))
                    continue;

                // Filter by pattern
                if (!string.IsNullOrEmpty(patternFilter))
                {
                    var patterns = patternFilter.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (!patterns.Any(p => p.Equals(pattern, StringComparison.OrdinalIgnoreCase)))
                        continue;
                }

                double val = schema.GetDouble(r, colFOverLA);
                val = SapUtils.ConvertLoadToKnPerM(val);

                string dir = schema.GetString(r, colDir) ?? "Gravity";
                string coordSys = schema.GetString(r, colCoordSys) ?? "GLOBAL";

                // Resolve direction
                var resolved = ResolveDirection(frameName, "Frame", dir, coordSys);

                loads.Add(new RawSapLoad
                {
                    ElementName = frameName,
                    LoadPattern = pattern,
                    Value1 = Math.Abs(val),
                    LoadType = "FrameDistributed",
                    Direction = resolved.ToString(),
                    GlobalAxis = resolved.GlobalAxis,
                    DirectionSign = resolved.Sign,
                    CoordSys = coordSys,
                    DistStart = schema.GetDouble(r, colAbsDistA),
                    DistEnd = schema.GetDouble(r, colAbsDistB),
                    ElementZ = frameGeomMap.ContainsKey(frameName) ? frameGeomMap[frameName] : 0
                });
            }

            return loads;
        }

        /// <summary>
        /// ??c Area Uniform Loads v?i Direction ?ã resolve
        /// </summary>
        public List<RawSapLoad> ReadAreaUniformLoads(string patternFilter = null)
        {
            var loads = new List<RawSapLoad>();
            var schema = GetTableSchema("Area Loads - Uniform", patternFilter);

            if (schema.RecordCount == 0) return loads;

            string colArea = schema.FindColumn("Area");
            string colPattern = schema.FindColumn("LoadPat", "OutputCase");
            string colDir = schema.FindColumn("Dir");
            string colCoordSys = schema.FindColumn("CoordSys");
            string colUnifLoad = schema.FindColumn("UnifLoad");

            if (colArea == null || colPattern == null) return loads;

            // Cache geometry
            var areaGeomMap = new Dictionary<string, double>();
            var areas = SapUtils.GetAllAreasGeometry();
            foreach (var a in areas) areaGeomMap[a.Name] = a.AverageZ;

            for (int r = 0; r < schema.RecordCount; r++)
            {
                string areaName = schema.GetString(r, colArea);
                string pattern = schema.GetString(r, colPattern);

                if (string.IsNullOrEmpty(areaName) || string.IsNullOrEmpty(pattern))
                    continue;

                if (!string.IsNullOrEmpty(patternFilter))
                {
                    var patterns = patternFilter.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (!patterns.Any(p => p.Equals(pattern, StringComparison.OrdinalIgnoreCase)))
                        continue;
                }

                double val = schema.GetDouble(r, colUnifLoad);
                val = SapUtils.ConvertLoadToKnPerM2(val);

                string dir = schema.GetString(r, colDir) ?? "Gravity";
                string coordSys = schema.GetString(r, colCoordSys) ?? "Local";

                var resolved = ResolveDirection(areaName, "Area", dir, coordSys);

                loads.Add(new RawSapLoad
                {
                    ElementName = areaName,
                    LoadPattern = pattern,
                    Value1 = Math.Abs(val),
                    LoadType = "AreaUniform",
                    Direction = resolved.ToString(),
                    GlobalAxis = resolved.GlobalAxis,
                    DirectionSign = resolved.Sign,
                    CoordSys = coordSys,
                    ElementZ = areaGeomMap.ContainsKey(areaName) ? areaGeomMap[areaName] : 0
                });
            }

            return loads;
        }

        /// <summary>
        /// ??c Joint Loads (Force) - ??Y ?? F1, F2, F3
        /// </summary>
        public List<RawSapLoad> ReadJointLoads(string patternFilter = null)
        {
            var loads = new List<RawSapLoad>();
            var schema = GetTableSchema("Joint Loads - Force", patternFilter);

            if (schema.RecordCount == 0) return loads;

            string colJoint = schema.FindColumn("Joint");
            string colPattern = schema.FindColumn("LoadPat", "OutputCase");
            string colCoordSys = schema.FindColumn("CoordSys");
            string colF1 = schema.FindColumn("F1");
            string colF2 = schema.FindColumn("F2");
            string colF3 = schema.FindColumn("F3");

            if (colJoint == null || colPattern == null) return loads;

            // Cache geometry
            var pointGeomMap = new Dictionary<string, double>();
            var points = SapUtils.GetAllPoints();
            foreach (var p in points) pointGeomMap[p.Name] = p.Z;

            for (int r = 0; r < schema.RecordCount; r++)
            {
                string joint = schema.GetString(r, colJoint);
                string pattern = schema.GetString(r, colPattern);
                string coordSys = schema.GetString(r, colCoordSys) ?? "GLOBAL";

                if (string.IsNullOrEmpty(joint) || string.IsNullOrEmpty(pattern))
                    continue;

                if (!string.IsNullOrEmpty(patternFilter))
                {
                    var patterns = patternFilter.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (!patterns.Any(p => p.Equals(pattern, StringComparison.OrdinalIgnoreCase)))
                        continue;
                }

                double f1 = schema.GetDouble(r, colF1);
                double f2 = schema.GetDouble(r, colF2);
                double f3 = schema.GetDouble(r, colF3);

                double z = pointGeomMap.ContainsKey(joint) ? pointGeomMap[joint] : 0;

                // Thêm 3 component riêng bi?t
                if (Math.Abs(f1) > 0.001)
                {
                    var resolved = ResolveDirection(joint, "Joint", "1", coordSys);
                    loads.Add(new RawSapLoad
                    {
                        ElementName = joint,
                        LoadPattern = pattern,
                        Value1 = Math.Abs(SapUtils.ConvertForceToKn(f1)),
                        LoadType = "PointForce",
                        Direction = resolved.ToString(),
                        GlobalAxis = resolved.GlobalAxis,
                        DirectionSign = resolved.Sign * Math.Sign(f1),
                        CoordSys = coordSys,
                        ElementZ = z
                    });
                }

                if (Math.Abs(f2) > 0.001)
                {
                    var resolved = ResolveDirection(joint, "Joint", "2", coordSys);
                    loads.Add(new RawSapLoad
                    {
                        ElementName = joint,
                        LoadPattern = pattern,
                        Value1 = Math.Abs(SapUtils.ConvertForceToKn(f2)),
                        LoadType = "PointForce",
                        Direction = resolved.ToString(),
                        GlobalAxis = resolved.GlobalAxis,
                        DirectionSign = resolved.Sign * Math.Sign(f2),
                        CoordSys = coordSys,
                        ElementZ = z
                    });
                }

                if (Math.Abs(f3) > 0.001)
                {
                    var resolved = ResolveDirection(joint, "Joint", "3", coordSys);
                    loads.Add(new RawSapLoad
                    {
                        ElementName = joint,
                        LoadPattern = pattern,
                        Value1 = Math.Abs(SapUtils.ConvertForceToKn(f3)),
                        LoadType = "PointForce",
                        Direction = resolved.ToString(),
                        GlobalAxis = resolved.GlobalAxis,
                        DirectionSign = resolved.Sign * Math.Sign(f3),
                        CoordSys = coordSys,
                        ElementZ = z
                    });
                }
            }

            return loads;
        }

        #endregion
    }
}
