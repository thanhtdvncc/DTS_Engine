using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
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

            // Hiển thị 10 dầm đầu tiên
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
        /// Test mapping tường với dầm
        /// </summary>
        [CommandMethod("DTS_TEST_MAP")]
        public void DTS_TEST_MAP()
        {
            WriteMessage("=== TEST MAPPING TƯỜNG - DẦM ===");

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

            // Nhập cao độ
            PromptDoubleOptions zOpt = new PromptDoubleOptions("\nNhập cao độ Z tường (mm, mặc định 0): ");
            zOpt.DefaultValue = 0;
            zOpt.AllowNegative = true;

            PromptDoubleResult zRes = Ed.GetDouble(zOpt);
            double wallZ = zRes.Status == PromptStatus.OK ? zRes.Value : 0;

            // Lấy frames từ SAP
            var frames = SapUtils.GetBeamsAtElevation(wallZ, MappingEngine.TOLERANCE_Z);
            WriteMessage($"Tìm thấy {frames.Count} dầm tại cao độ {wallZ}");

            if (frames.Count == 0)
            {
                WriteError("Không có dầm nào ở cao độ này!");
                return;
            }

            // Chuẩn bị layer
            AcadUtils.CreateLayer(MAPPING_LAYER, 4);
            AcadUtils.ClearLayer(MAPPING_LAYER);

            int mappedCount = 0;
            int unmappedCount = 0;

            UsingTransaction(tr =>
            {
                foreach (ObjectId lineId in lineIds)
                {
                    Line lineEnt = tr.GetObject(lineId, OpenMode.ForWrite) as Line;
                    if (lineEnt == null) continue;

                    Point2D startPt = new Point2D(lineEnt.StartPoint.X, lineEnt.StartPoint.Y);
                    Point2D endPt = new Point2D(lineEnt.EndPoint.X, lineEnt.EndPoint.Y);

                    // Thực hiện mapping
                    var result = MappingEngine.FindMappings(startPt, endPt, wallZ, frames);

                    string handle = lineId.Handle.ToString();

                    if (result.HasMapping)
                    {
                        WriteMessage($"[{handle}]: {result.GetLabelText("", "", 0)}");
                        lineEnt.ColorIndex = 3; // Xanh lá

                        // Cập nhật WallData
                        var wData = XDataUtils.ReadWallData(lineEnt) ?? new WallData();
                        wData.Mappings = result.Mappings;
                        XDataUtils.SaveWallData(lineEnt, wData, tr);

                        mappedCount++;
                    }
                    else
                    {
                        WriteMessage($"[{handle}]: Không tìm thấy dầm phù hợp -> NEW");
                        lineEnt.ColorIndex = 1; // Đỏ
                        unmappedCount++;
                    }
                }
            });

            WriteMessage($"\nKết quả: {mappedCount} mapped, {unmappedCount} unmapped");
        }

        /// <summary>
        /// Gán tải lên SAP2000
        /// </summary>
        [CommandMethod("DTS_ASSIGN_LOAD")]
        public void DTS_ASSIGN_LOAD()
        {
            WriteMessage("=== GÁN TẢI LÊN SAP2000 ===");

            // Kiểm tra kết nối
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

            // Nhập load pattern
            PromptStringOptions patternOpt = new PromptStringOptions("\nNhập Load Pattern (mặc định DL): ");
            patternOpt.DefaultValue = "DL";
            PromptResult patternRes = Ed.GetString(patternOpt);
            string loadPattern = string.IsNullOrEmpty(patternRes.StringResult) ? "DL" : patternRes.StringResult;

            // Kiểm tra pattern tồn tại
            if (!SapUtils.LoadPatternExists(loadPattern))
            {
                WriteError($"Load pattern '{loadPattern}' không tồn tại trong SAP2000!");
                return;
            }

            int assignedCount = 0;
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
                        if (mapping.TargetFrame == "New") continue;

                        bool success = SapUtils.AssignLoadFromMapping(mapping, loadPattern, wData.LoadValue.Value);

                        if (success)
                        {
                            WriteMessage($"[{lineId.Handle}] -> {mapping.TargetFrame}: {wData.LoadValue.Value:0. 00} kN/m");
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
            WriteMessage($"\nKết quả: {assignedCount} thành công, {failedCount} thất bại");
        }
    }
}