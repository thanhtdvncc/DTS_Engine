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
        /// Đồng bộ hình học Tường - Dầm (Geometric Mapping Only)
        /// Không yêu cầu nhập độ dày hay tải trọng trước.
        /// </summary>
        [CommandMethod("DTS_SYNC_SAP")]
        public void DTS_SYNC_SAP()
        {
            WriteMessage("=== ĐỒNG BỘ MAPPING HÌNH HỌC (GEOMETRIC SYNC) ===");

            // 1. Kết nối SAP
            if (!SapUtils.IsConnected)
            {
                if (!SapUtils.Connect(out string msg)) { WriteError(msg); return; }
            }
            var allFrames = SapUtils.GetAllFramesGeometry();
            if (allFrames.Count == 0) { WriteError("Model SAP2000 trống!"); return; }

            // 2. Chọn tường
            var lineIds = AcadUtils.SelectObjectsOnScreen("LINE");
            if (lineIds.Count == 0) return;

            // 3. Chuẩn bị layer nhãn
            AcadUtils.CreateLayer(LABEL_LAYER, 254);
            AcadUtils.ClearLayer(LABEL_LAYER);

            int processed = 0;
            int mappedCount = 0;

            UsingTransaction(tr =>
            {
                foreach (ObjectId lineId in lineIds)
                {
                    Line lineEnt = tr.GetObject(lineId, OpenMode.ForWrite) as Line;
                    if (lineEnt == null) continue;

                    // Lọc sơ bộ Layer
                    if (!lineEnt.Layer.Equals(WALL_LAYER, System.StringComparison.OrdinalIgnoreCase)) continue;

                    // Đọc hoặc khởi tạo WallData mới nếu chưa có
                    WallData wData = XDataUtils.ReadWallData(lineEnt) ?? new WallData();

                    // --- KIỂM TRA LIÊN KẾT (BẮT BUỘC) ---
                    // Chỉ cần biết "Con của ai" để tính Z và tọa độ tương đối
                    if (string.IsNullOrEmpty(wData.OriginHandle))
                    {
                        continue; // Bỏ qua âm thầm những thanh chưa Link
                    }

                    // --- TÌM CHA ĐỂ LẤY THAM CHIẾU ---
                    ObjectId originId = AcadUtils.GetObjectIdFromHandle(wData.OriginHandle);
                    if (originId == ObjectId.Null || originId.IsErased) continue;

                    DBObject originObj = tr.GetObject(originId, OpenMode.ForRead);
                    StoryData storyData = XDataUtils.ReadStoryData(originObj);
                    if (storyData == null) continue;

                    // --- CHUẨN BỊ DỮ LIỆU MAPPING ---
                    // 1. Cao độ Z chuẩn để lọc dầm
                    double wallZ = storyData.Elevation;

                    // 2. Vector dịch chuyển (Offset) từ CAD sang SAP
                    Point2D insertOffset = new Point2D(storyData.OffsetX, storyData.OffsetY);

                    // 3. "Độ dày tìm kiếm" (Search Thickness)
                    // Đây không phải độ dày tường thực tế, mà là phạm vi tìm kiếm hình học.
                    // Ta giả định một vùng tìm kiếm mặc định (ví dụ 220mm) để Engine quét.
                    double searchScope = 220.0;

                    // --- THỰC HIỆN MAPPING ---
                    // Lọc dầm ở cao độ Z (dung sai Z=200mm)
                    var framesAtZ = allFrames
                        .Where(f => f.IsBeam && System.Math.Abs(f.AverageZ - wallZ) <= MappingEngine.TOLERANCE_Z)
                        .ToList();

                    Point2D startPt = new Point2D(lineEnt.StartPoint.X, lineEnt.StartPoint.Y);
                    Point2D endPt = new Point2D(lineEnt.EndPoint.X, lineEnt.EndPoint.Y);

                    var result = MappingEngine.FindMappings(
                        startPt, endPt, wallZ, framesAtZ,
                        insertOffset, searchScope);

                    result.WallHandle = lineId.Handle.ToString();

                    // --- LƯU KẾT QUẢ ---
                    // Chỉ cập nhật Mappings và BaseZ. 
                    // KHÔNG động chạm đến Thickness hay LoadValue (giữ nguyên null nếu chưa có)
                    wData.Mappings = result.Mappings;
                    wData.BaseZ = wallZ;

                    // Lưu lại WallData (lúc này có thể chỉ chứa Link và Mappings)
                    XDataUtils.SaveWallData(lineEnt, wData, tr);

                    // --- HIỂN THỊ TRỰC QUAN ---
                    lineEnt.ColorIndex = result.GetColorIndex();

                    // Vẽ nhãn (Logic vẽ sẽ tự động thích ứng nếu thiếu data tải)
                    LabelUtils.UpdateWallLabels(lineId, wData, result, tr);

                    processed++;
                    if (result.HasMapping) mappedCount++;
                }
            });

            WriteMessage($"\nĐã Mapping xong {processed} tường. ({mappedCount} có dầm đỡ)");
            WriteMessage("Mẹo: Dùng lệnh [DTS_SET] để gán tải trọng sau khi đã kiểm tra Mapping.");
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