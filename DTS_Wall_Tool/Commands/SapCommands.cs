using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using DTS_Wall_Tool.Core.Data;
using DTS_Wall_Tool.Core.Engines;
using DTS_Wall_Tool.Core.Primitives;
using DTS_Wall_Tool.Core.Utils;

namespace DTS_Wall_Tool.Commands
{
    /// <summary>
    /// Các lệnh làm việc với SAP2000
    /// </summary>
    public class SapCommands : CommandBase
    {
        private const string MAPPING_LAYER = "dts_mapping";
        private const string LABEL_LAYER = "dts_frame_label";

        /// <summary>
        /// Kiểm tra kết nối SAP2000
        /// </summary>
        [CommandMethod("DTS_TEST_SAP")]
        public void DTS_TEST_SAP()
        {
            WriteMessage("=== KIỂM TRA KẾT NỐI SAP2000 ===");

            bool connected = SapUtils.Connect(out string message);
            WriteMessage(message);

            if (connected)
            {
                int frameCount = SapUtils.CountFrames();
                WriteMessage($"Số lượng Frame trong model: {frameCount}");

                var stories = SapUtils.GetStories();
                WriteMessage($"Số lượng tầng: {stories.Count}");
                foreach (var story in stories)
                {
                    WriteMessage($"  - {story}");
                }
            }
        }

        /// <summary>
        /// Lấy danh sách frames từ SAP2000
        /// </summary>
        [CommandMethod("DTS_GET_FRAMES")]
        public void DTS_GET_FRAMES()
        {
            WriteMessage("=== LẤY FRAMES TỪ SAP2000 ===");

            if (!SapUtils.IsConnected)
            {
                bool connected = SapUtils.Connect(out string msg);
                if (!connected)
                {
                    WriteError(msg);
                    return;
                }
            }

            var frames = SapUtils.GetAllFramesGeometry();
            WriteMessage($"Tổng số Frame: {frames.Count}");

            int beamCount = 0;
            int columnCount = 0;

            foreach (var frame in frames)
            {
                if (frame.IsBeam)
                    beamCount++;
                else
                    columnCount++;
            }

            WriteMessage($"  - Dầm: {beamCount}");
            WriteMessage($"  - Cột: {columnCount}");

            WriteMessage("\n10 dầm đầu tiên:");
            int count = 0;
            foreach (var frame in frames)
            {
                if (frame.IsBeam)
                {
                    WriteMessage($"  {frame}");
                    count++;
                    if (count >= 10) break;
                }
            }
        }

        /// <summary>
        /// Test mapping tường với dầm (Khôi phục từ VBA btnCombineWithSAP)
        /// Chiến thuật: Hình chiếu & Chồng lấn (Projection & Overlap)
        /// </summary>
        [CommandMethod("DTS_TEST_MAP")]
        public void DTS_TEST_MAP()
        {
            WriteMessage("=== MAPPING TƯỜNG - DẦM (Projection & Overlap) ===");

            // Kết nối SAP
            if (!SapUtils.IsConnected)
            {
                bool connected = SapUtils.Connect(out string msg);
                if (!connected)
                {
                    WriteError(msg);
                    return;
                }
            }

            // Chọn tường
            var lineIds = AcadUtils.SelectObjectsOnScreen("LINE");
            if (lineIds.Count == 0)
            {
                WriteMessage("Không có tường nào được chọn.");
                return;
            }

            WriteMessage($"Đã chọn {lineIds.Count} tường.");

            // Nhập cao độ
            PromptDoubleOptions zOpt = new PromptDoubleOptions("\nNhập cao độ Z tường (mm, mặc định 0): ");
            zOpt.DefaultValue = 0;
            zOpt.AllowNegative = true;

            PromptDoubleResult zRes = Ed.GetDouble(zOpt);
            double wallZ = zRes.Status == PromptStatus.OK ? zRes.Value : 0;

            // Lấy Insertion Point Offset
            Point2D insertOffset = GetInsertionOffset();

            // Lấy frames từ SAP
            var frames = SapUtils.GetBeamsAtElevation(wallZ, MappingEngine.TOLERANCE_Z);
            WriteMessage($"Tìm thấy {frames.Count} dầm tại cao độ {wallZ}");

            if (frames.Count == 0)
            {
                WriteError("Không có dầm nào ở cao độ này!");
                return;
            }

            // Tạo layers
            AcadUtils.CreateLayer(MAPPING_LAYER, 4);
            AcadUtils.CreateLayer(LABEL_LAYER, 254);

            int mappedCount = 0;
            int unmappedCount = 0;
            int partialCount = 0;

            UsingTransaction(tr =>
            {
                // Lấy BlockTableRecord để thêm MText
                var bt = tr.GetObject(Db.BlockTableId, OpenMode.ForRead) as BlockTable;
                var btr = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                foreach (ObjectId lineId in lineIds)
                {
                    Line lineEnt = tr.GetObject(lineId, OpenMode.ForWrite) as Line;
                    if (lineEnt == null) continue;

                    Point2D startPt = new Point2D(lineEnt.StartPoint.X, lineEnt.StartPoint.Y);
                    Point2D endPt = new Point2D(lineEnt.EndPoint.X, lineEnt.EndPoint.Y);

                    // Đọc thông tin tường hiện có
                    var wData = XDataUtils.ReadWallData(lineEnt) ?? new WallData();
                    double wallThickness = wData.Thickness ?? 200.0;
                    string wallType = wData.WallType ?? $"W{wallThickness:0}";
                    string loadPattern = wData.LoadPattern ?? "DL";
                    double loadValue = wData.LoadValue ?? 0;

                    // Thực hiện mapping
                    var result = MappingEngine.FindMappings(
                        startPt, endPt, wallZ, frames,
                        insertOffset, wallThickness);

                    result.WallHandle = lineId.Handle.ToString();

                    string handle = result.WallHandle;

                    // Xác định màu và cập nhật line
                    int colorIndex = result.GetColorIndex();
                    lineEnt.ColorIndex = colorIndex;

                    if (result.HasMapping)
                    {
                        bool isFull = result.IsFullyCovered;
                        string matchInfo = isFull ? "FULL" : "PARTIAL";

                        WriteMessage($"[{handle}]: {result.GetLabelText(wallType, loadPattern, loadValue)} ({matchInfo}, {result.CoveragePercent:0}% coverage)");

                        // Cập nhật WallData
                        wData.Mappings = result.Mappings;
                        XDataUtils.SaveWallData(lineEnt, wData, tr);

                        // Vẽ MText Labels
                        // Dòng trên: Thông tin mapping (-> FrameName)
                        string topLabel = result.GetTopLabelText();
                        LabelPlotter.PlotLabel(btr, tr, startPt, endPt, topLabel,
                            LabelPosition.MiddleTop, 60, LABEL_LAYER);

                        // Dòng dưới: Thông tin load (W200 DL=7. 20)
                        string bottomLabel = result.GetBottomLabelText(wallType, loadPattern, loadValue);
                        LabelPlotter.PlotLabel(btr, tr, startPt, endPt, bottomLabel,
                            LabelPosition.MiddleBottom, 60, LABEL_LAYER);

                        if (isFull)
                            mappedCount++;
                        else
                            partialCount++;
                    }
                    else
                    {
                        WriteMessage($"[{handle}]: Không tìm thấy dầm phù hợp -> NEW");

                        // Tạo mapping NEW
                        wData.Mappings = new List<MappingRecord>
                        {
                            new MappingRecord
                            {
                                TargetFrame = "New",
                                MatchType = "NEW",
                                CoveredLength = result.WallLength
                            }
                        };
                        XDataUtils.SaveWallData(lineEnt, wData, tr);

                        // Vẽ MText Labels cho NEW
                        string topLabel = "{\\C1;-> NEW}";
                        LabelPlotter.PlotLabel(btr, tr, startPt, endPt, topLabel,
                            LabelPosition.MiddleTop, 60, LABEL_LAYER);

                        string bottomLabel = $"{{\\C1;{wallType} {loadPattern}={loadValue:0.00}}}";
                        LabelPlotter.PlotLabel(btr, tr, startPt, endPt, bottomLabel,
                            LabelPosition.MiddleBottom, 60, LABEL_LAYER);

                        unmappedCount++;
                    }
                }
            });

            // Thống kê
            WriteMessage("\n=== KẾT QUẢ ===");
            WriteMessage($"  Full Match (Xanh):    {mappedCount}");
            WriteMessage($"  Partial Match (Vàng): {partialCount}");
            WriteMessage($"  New (Đỏ):             {unmappedCount}");
            WriteMessage($"  Tổng:                 {lineIds.Count}");
        }

        /// <summary>
        /// Gán tải lên SAP2000
        /// </summary>
        [CommandMethod("DTS_ASSIGN_LOAD")]
        public void DTS_ASSIGN_LOAD()
        {
            WriteMessage("=== GÁN TẢI LÊN SAP2000 ===");

            if (!SapUtils.IsConnected)
            {
                bool connected = SapUtils.Connect(out string msg);
                if (!connected)
                {
                    WriteError(msg);
                    return;
                }
            }

            var lineIds = AcadUtils.SelectObjectsOnScreen("LINE");
            if (lineIds.Count == 0)
            {
                WriteMessage("Không có tường nào được chọn.");
                return;
            }

            PromptStringOptions patternOpt = new PromptStringOptions("\nNhập Load Pattern (mặc định DL): ");
            patternOpt.DefaultValue = "DL";
            PromptResult patternRes = Ed.GetString(patternOpt);
            string loadPattern = string.IsNullOrEmpty(patternRes.StringResult) ? "DL" : patternRes.StringResult;

            if (!SapUtils.LoadPatternExists(loadPattern))
            {
                WriteError($"Load pattern '{loadPattern}' không tồn tại trong SAP2000!");
                return;
            }

            int assignedCount = 0;
            int createdCount = 0;
            int failedCount = 0;

            UsingTransaction(tr =>
            {
                foreach (ObjectId lineId in lineIds)
                {
                    DBObject lineObj = tr.GetObject(lineId, OpenMode.ForRead);
                    WallData wData = XDataUtils.ReadWallData(lineObj);

                    if (wData == null || !wData.LoadValue.HasValue)
                    {
                        WriteMessage($"[{lineId.Handle}]: Chưa có thông tin tải - bỏ qua");
                        continue;
                    }

                    if (wData.Mappings == null || wData.Mappings.Count == 0)
                    {
                        WriteMessage($"[{lineId.Handle}]: Chưa có mapping - bỏ qua");
                        continue;
                    }

                    foreach (var mapping in wData.Mappings)
                    {
                        if (mapping.TargetFrame == "New")
                        {
                            WriteMessage($"[{lineId.Handle}]: Frame NEW - cần tạo thủ công");
                            createdCount++;
                            continue;
                        }

                        bool success = SapUtils.AssignDistributedLoad(
                            mapping.TargetFrame,
                            loadPattern,
                            wData.LoadValue.Value,
                            mapping.DistI,
                            mapping.DistJ,
                            false
                        );

                        if (success)
                        {
                            WriteMessage($"[{lineId.Handle}] -> {mapping.TargetFrame}: {wData.LoadValue.Value:0.00} kN/m [{mapping.DistI:0}-{mapping.DistJ:0}mm]");
                            assignedCount++;
                        }
                        else
                        {
                            WriteError($"[{lineId.Handle}] -> {mapping.TargetFrame}: LỖI gán tải");
                            failedCount++;
                        }
                    }
                }
            });

            SapUtils.RefreshView();
            WriteMessage($"\nKết quả: {assignedCount} thành công, {createdCount} cần tạo mới, {failedCount} thất bại");
        }

        #region Helper Methods

        private Point2D GetInsertionOffset()
        {
            var originCircle = AcadUtils.FindOriginCircle();
            if (originCircle != null)
            {
                WriteMessage($"Sử dụng Origin Circle: ({originCircle.Value.X:0. 0}, {originCircle.Value.Y:0.0})");
                return originCircle.Value;
            }

            PromptPointOptions ptOpt = new PromptPointOptions("\nChọn điểm gốc (hoặc Enter để dùng 0,0): ");
            ptOpt.AllowNone = true;
            PromptPointResult ptRes = Ed.GetPoint(ptOpt);

            if (ptRes.Status == PromptStatus.OK)
            {
                return new Point2D(ptRes.Value.X, ptRes.Value.Y);
            }

            return new Point2D(0, 0);
        }

        #endregion
    }
}