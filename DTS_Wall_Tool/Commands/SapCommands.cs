using System.Collections.Generic;
using System.Linq;
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
        private const string LABEL_LAYER = "dts_frame_label";
        private const string WALL_LAYER = "DTS_WALL_DIAGRAM";

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
                if (frame.IsBeam) beamCount++;
                else columnCount++;
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
        /// Đồng bộ tường với dầm SAP2000 (Smart Link)
        /// Luồng xử lý:
        /// 1. Kết nối SAP, lấy tất cả dầm
        /// 2. Quét chọn tường
        /// 3.  LỌC RÁC: Chỉ xử lý đối tượng có XData DTS_APP và đã Link với Origin
        /// 4. TÌM CHA: Lấy tọa độ gốc và cao độ Z từ Origin cha
        /// 5.  MAPPING: Chạy engine so khớp
        /// 6. VISUAL: Đổi màu line + Vẽ nhãn
        /// </summary>
        [CommandMethod("DTS_SYNC_SAP")]
        public void DTS_SYNC_SAP()
        {
            WriteMessage("=== ĐỒNG BỘ TƯỜNG - DẦM SAP2000 (Smart Link) ===");

            // ========== BƯỚC 1: KẾT NỐI SAP ==========
            if (!SapUtils.IsConnected)
            {
                bool connected = SapUtils.Connect(out string msg);
                if (!connected)
                {
                    WriteError(msg);
                    return;
                }
            }

            // Lấy TẤT CẢ frames từ SAP (sẽ lọc theo Z sau)
            var allFrames = SapUtils.GetAllFramesGeometry();
            if (allFrames.Count == 0)
            {
                WriteError("Không có frame nào trong model SAP2000!");
                return;
            }
            WriteMessage($"SAP2000: {allFrames.Count} frames (tất cả cao độ)");

            // ========== BƯỚC 2: CHỌN TƯỜNG ==========
            var lineIds = AcadUtils.SelectObjectsOnScreen("LINE");
            if (lineIds.Count == 0)
            {
                WriteMessage("Không có đối tượng nào được chọn.");
                return;
            }
            WriteMessage($"Đã chọn {lineIds.Count} đối tượng.");

            // ========== BƯỚC 3-6: XỬ LÝ TỪNG TƯỜNG ==========
            int validCount = 0;
            int skippedNoXData = 0;
            int skippedNoLink = 0;
            int skippedNoOrigin = 0;
            int mappedFull = 0;
            int mappedPartial = 0;
            int mappedNew = 0;

            // Tạo layer cho label
            AcadUtils.CreateLayer(LABEL_LAYER, 254);
            // Xóa label cũ trên layer
            AcadUtils.ClearLayer(LABEL_LAYER);

            UsingTransaction(tr =>
            {
                // Lấy BlockTableRecord
                var bt = tr.GetObject(Db.BlockTableId, OpenMode.ForRead) as BlockTable;
                var btr = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                foreach (ObjectId lineId in lineIds)
                {
                    Line lineEnt = tr.GetObject(lineId, OpenMode.ForWrite) as Line;
                    if (lineEnt == null) continue;

                    // --- LỌC 1: Kiểm tra layer ---
                    if (!lineEnt.Layer.Equals(WALL_LAYER, System.StringComparison.OrdinalIgnoreCase))
                    {
                        // Không phải layer tường, bỏ qua im lặng
                        skippedNoXData++;
                        continue;
                    }

                    // --- LỌC 2: Kiểm tra có XData DTS_APP không ---
                    WallData wData = XDataUtils.ReadWallData(lineEnt);
                    if (wData == null || !wData.HasValidData())
                    {
                        skippedNoXData++;
                        continue;
                    }

                    // --- LỌC 3: Kiểm tra đã Link với Origin chưa ---
                    if (string.IsNullOrEmpty(wData.OriginHandle))
                    {
                        skippedNoLink++;
                        continue;
                    }

                    // --- TÌM CHA: Lấy thông tin Origin ---
                    ObjectId originId = AcadUtils.GetObjectIdFromHandle(wData.OriginHandle);
                    if (originId == ObjectId.Null)
                    {
                        WriteMessage($"[{lineId.Handle}]: Origin handle không hợp lệ - bỏ qua");
                        skippedNoOrigin++;
                        continue;
                    }

                    DBObject originObj = tr.GetObject(originId, OpenMode.ForRead);
                    StoryData storyData = XDataUtils.ReadStoryData(originObj);
                    if (storyData == null)
                    {
                        WriteMessage($"[{lineId.Handle}]: Origin không có StoryData - bỏ qua");
                        skippedNoOrigin++;
                        continue;
                    }

                    // Lấy cao độ Z từ Origin
                    double wallZ = storyData.Elevation;

                    // Lấy tọa độ gốc từ Origin (để tính offset)
                    Point2D insertOffset = new Point2D(storyData.OffsetX, storyData.OffsetY);

                    // Nếu Origin là Circle, lấy tâm làm offset
                    if (originObj is Circle circle)
                    {
                        insertOffset = new Point2D(circle.Center.X, circle.Center.Y);
                    }

                    // --- LỌC theo cao độ để lấy dầm phù hợp ---
                    var beamsAtZ = allFrames
                        .Where(f => f.IsBeam && System.Math.Abs(f.AverageZ - wallZ) <= MappingEngine.TOLERANCE_Z)
                        .ToList();

                    if (beamsAtZ.Count == 0)
                    {
                        WriteMessage($"[{lineId.Handle}]: Không có dầm ở cao độ Z={wallZ:0} - NEW");
                        // Vẫn xử lý như NEW
                    }

                    // --- MAPPING ---
                    Point2D startPt = new Point2D(lineEnt.StartPoint.X, lineEnt.StartPoint.Y);
                    Point2D endPt = new Point2D(lineEnt.EndPoint.X, lineEnt.EndPoint.Y);
                    double wallThickness = wData.Thickness ?? 200.0;

                    var result = MappingEngine.FindMappings(
                        startPt, endPt, wallZ, beamsAtZ,
                        insertOffset, wallThickness);

                    result.WallHandle = lineId.Handle.ToString();

                    // --- CẬP NHẬT WALLDATA ---
                    wData.Mappings = result.Mappings;
                    wData.BaseZ = wallZ;
                    XDataUtils.SaveWallData(lineEnt, wData, tr);

                    // --- VISUAL: Đổi màu line ---
                    int colorIndex = result.GetColorIndex();
                    lineEnt.ColorIndex = colorIndex;

                    // --- VISUAL: Vẽ nhãn ---
                    LabelUtils.UpdateWallLabels(lineId, wData, result, tr);

                    // --- THỐNG KÊ ---
                    validCount++;
                    if (result.HasMapping)
                    {
                        if (result.IsFullyCovered)
                        {
                            mappedFull++;
                            WriteMessage($"[{lineId.Handle}]: -> {result.Mappings[0].TargetFrame} (FULL, Z={wallZ:0})");
                        }
                        else
                        {
                            mappedPartial++;
                            WriteMessage($"[{lineId.Handle}]: -> {result.Mappings[0].TargetFrame} (PARTIAL, Z={wallZ:0})");
                        }
                    }
                    else
                    {
                        mappedNew++;
                        WriteMessage($"[{lineId.Handle}]: -> NEW (Z={wallZ:0})");
                    }
                }
            });

            // ========== THỐNG KÊ KẾT QUẢ ==========
            WriteMessage("\n=== KẾT QUẢ ĐỒNG BỘ ===");
            WriteMessage($"  Tổng chọn:              {lineIds.Count}");
            WriteMessage($"  Đã xử lý (có Link):     {validCount}");
            WriteMessage($"    - Full Match (Xanh):  {mappedFull}");
            WriteMessage($"    - Partial (Vàng):     {mappedPartial}");
            WriteMessage($"    - New (Đỏ):           {mappedNew}");
            WriteMessage($"  Bỏ qua:");
            WriteMessage($"    - Không có XData:     {skippedNoXData}");
            WriteMessage($"    - Chưa Link Origin:   {skippedNoLink}");
            WriteMessage($"    - Origin lỗi:         {skippedNoOrigin}");

            if (skippedNoLink > 0)
            {
                WriteMessage("\n[GỢI Ý] Có đối tượng chưa Link với Origin.");
                WriteMessage("        Chạy lệnh DTS_SET_ORIGIN để thiết lập Origin cho tầng.");
                WriteMessage("        Sau đó chạy DTS_LINK để liên kết tường với Origin.");
            }
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
    }
}