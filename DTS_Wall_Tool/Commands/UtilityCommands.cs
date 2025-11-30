using Autodesk.AutoCAD.Runtime;
using DTS_Wall_Tool.Core.Engines;
using DTS_Wall_Tool.Core.Utils;

namespace DTS_Wall_Tool.Commands
{
    /// <summary>
    /// Các lệnh tiện ích
    /// </summary>
    public class UtilityCommands : CommandBase
    {
        /// <summary>
        /// Lệnh test cơ bản
        /// </summary>
        [CommandMethod("DTS_HELLO")]
        public void DTS_HELLO()
        {
            WriteMessage("=== DTS WALL TOOL ===");
            WriteMessage("Version: 2.0");
            WriteMessage("Author: DTS Engineering");
            WriteMessage("thanhtdvncc");
            WriteMessage("");
            WriteMessage("Commands:");
            WriteMessage("  DTS_SCAN      - Quét thông tin tường");
            WriteMessage("  DTS_SET       - Gán thông tin tường");
            WriteMessage("  DTS_CLEAR     - Xóa thông tin tường");
            WriteMessage("  DTS_LINK      - Liên kết tường với origin");
            WriteMessage("  DTS_SHOW_LINK - Hiển thị liên kết");
            WriteMessage("  DTS_BREAK_LINK- Xóa liên kết");
            WriteMessage("  DTS_SET_ORIGIN- Thiết lập origin");
            WriteMessage("  DTS_TEST_SAP  - Kiểm tra kết nối SAP2000");
            WriteMessage("  DTS_GET_FRAMES- Lấy danh sách frames");
            WriteMessage("  DTS_TEST_MAP  - Test mapping tường-dầm");
            WriteMessage("  DTS_ASSIGN_LOAD - Gán tải lên SAP2000");
            WriteMessage("  DTS_CALC_LOAD - Tính tải trọng tường");
        }

        /// <summary>
        /// Tính tải trọng tường
        /// </summary>
        [CommandMethod("DTS_CALC_LOAD")]
        public void DTS_CALC_LOAD()
        {
            WriteMessage("=== TÍNH TẢI TRỌNG TƯỜNG ===");

            // Hiển thị bảng tra nhanh
            var loadTable = LoadCalculator.GetQuickLoadTable();

            WriteMessage("\nBảng tra nhanh (chiều cao 3300mm, trừ dầm 400mm):");
            WriteMessage("---------------------------------------");
            WriteMessage("| Độ dày (mm) | Tải (kN/m) |");
            WriteMessage("---------------------------------------");

            foreach (var item in loadTable)
            {
                WriteMessage($"| {item.Key,11} | {item.Value,10:0.00} |");
            }

            WriteMessage("---------------------------------------");

            // Cho phép nhập tùy chỉnh
            var thicknessOpt = new Autodesk.AutoCAD.EditorInput.PromptDoubleOptions("\nNhập độ dày tường để tính (mm, 0 để bỏ qua): ")
            {
                DefaultValue = 0,
                AllowNegative = false
            };

            var thicknessRes = Ed.GetDouble(thicknessOpt);
            if (thicknessRes.Status == Autodesk.AutoCAD.EditorInput.PromptStatus.OK && thicknessRes.Value > 0)
            {
                var calc = new LoadCalculator();
                double load = calc.CalculateLineLoadWithDeduction(thicknessRes.Value);
                WriteMessage($"\nTường {thicknessRes.Value}mm: {load:0. 00} kN/m");
            }
        }

        /// <summary>
        /// Xóa tất cả layer tạm
        /// </summary>
        [CommandMethod("DTS_CLEANUP")]
        public void DTS_CLEANUP()
        {
            WriteMessage("=== DỌN DẸP LAYER TẠM ===");

            string[] tempLayers = { "dts_linkmap", "dts_highlight", "dts_mapping", "dts_labels", "dts_temp" };

            foreach (var layer in tempLayers)
            {
                AcadUtils.ClearLayer(layer);
            }

            WriteSuccess("Đã xóa tất cả layer tạm.");
        }

        /// <summary>
        /// Hiển thị thống kê
        /// </summary>
        [CommandMethod("DTS_STATS")]
        public void DTS_STATS()
        {
            WriteMessage("=== THỐNG KÊ BẢN VẼ ===");

            var lineIds = AcadUtils.SelectAll("LINE");
            var circleIds = AcadUtils.SelectAll("CIRCLE");

            int totalLines = lineIds.Count;
            int wallsWithData = 0;
            int wallsWithMapping = 0;
            int origins = 0;

            UsingTransaction(tr =>
            {
                foreach (var lineId in lineIds)
                {
                    var obj = tr.GetObject(lineId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
                    var wData = XDataUtils.ReadWallData(obj);
                    if (wData != null && wData.HasValidData())
                    {
                        wallsWithData++;
                        if (wData.Mappings != null && wData.Mappings.Count > 0)
                            wallsWithMapping++;
                    }
                }

                foreach (var circleId in circleIds)
                {
                    var obj = tr.GetObject(circleId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
                    var sData = XDataUtils.ReadStoryData(obj);
                    if (sData != null)
                        origins++;
                }
            });

            WriteMessage($"  Tổng số LINE: {totalLines}");
            WriteMessage($"  Tường có data: {wallsWithData}");
            WriteMessage($"  Tường có mapping: {wallsWithMapping}");
            WriteMessage($"  Origins: {origins}");
        }
    }
}