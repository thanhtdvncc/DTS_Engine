using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using DTS_Wall_Tool.Core.Data;
using DTS_Wall_Tool.Core.Utils;

namespace DTS_Wall_Tool.Commands
{
    /// <summary>
    /// Các lệnh scan và set dữ liệu tường
    /// </summary>
    public class ScanCommands : CommandBase
    {
        /// <summary>
        /// Quét và hiển thị thông tin tường
        /// </summary>
        [CommandMethod("DTS_SCAN")]
        public void DTS_SCAN()
        {
            WriteMessage("=== QUÉT THÔNG TIN TƯỜNG ===");

            var lineIds = AcadUtils.SelectObjectsOnScreen("LINE");
            if (lineIds.Count == 0)
            {
                WriteMessage("Không có đối tượng nào được chọn.");
                return;
            }

            int hasData = 0;
            int noData = 0;

            UsingTransaction(tr =>
            {
                foreach (ObjectId id in lineIds)
                {
                    DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                    WallData wData = XDataUtils.ReadWallData(obj);

                    string handle = id.Handle.ToString();

                    if (wData != null && wData.HasValidData())
                    {
                        WriteMessage($"  [{handle}]: {wData}");
                        hasData++;
                    }
                    else
                    {
                        noData++;
                    }
                }
            });

            WriteMessage($"Tổng: {lineIds.Count} | Có data: {hasData} | Chưa có: {noData}");
        }

        /// <summary>
        /// Set thông tin tường cho các line được chọn
        /// </summary>
        [CommandMethod("DTS_SET")]
        public void DTS_SET()
        {
            WriteMessage("=== GÁN THÔNG TIN TƯỜNG ===");

            var lineIds = AcadUtils.SelectObjectsOnScreen("LINE");
            if (lineIds.Count == 0)
            {
                WriteMessage("Không có đối tượng nào được chọn.");
                return;
            }

            // Nhập độ dày
            PromptDoubleOptions thicknessOpt = new PromptDoubleOptions("\nNhập độ dày tường (mm): ");
            thicknessOpt.DefaultValue = 220;
            thicknessOpt.AllowNegative = false;
            thicknessOpt.AllowZero = false;

            PromptDoubleResult thicknessRes = Ed.GetDouble(thicknessOpt);
            if (thicknessRes.Status != PromptStatus.OK)
            {
                WriteMessage("Đã hủy.");
                return;
            }
            double thickness = thicknessRes.Value;

            // Nhập loại tường
            PromptStringOptions typeOpt = new PromptStringOptions("\nNhập loại tường (Enter để tự động): ");
            typeOpt.AllowSpaces = false;
            typeOpt.DefaultValue = "";

            PromptResult typeRes = Ed.GetString(typeOpt);
            string wallType = typeRes.StringResult;

            if (string.IsNullOrEmpty(wallType))
            {
                wallType = "W" + ((int)thickness).ToString();
            }

            // Gán dữ liệu
            int count = 0;
            UsingTransaction(tr =>
            {
                foreach (ObjectId id in lineIds)
                {
                    DBObject obj = tr.GetObject(id, OpenMode.ForWrite);

                    WallData wData = new WallData
                    {
                        Thickness = thickness,
                        WallType = wallType,
                        LoadPattern = "DL"
                    };

                    XDataUtils.SaveWallData(obj, wData, tr);
                    count++;
                }
            });

            WriteSuccess($"Đã gán thông tin cho {count} tường: {wallType} (T={thickness}mm)");
        }

        /// <summary>
        /// Xóa thông tin tường
        /// </summary>
        [CommandMethod("DTS_CLEAR")]
        public void DTS_CLEAR()
        {
            WriteMessage("=== XÓA THÔNG TIN TƯỜNG ===");

            var lineIds = AcadUtils.SelectObjectsOnScreen("LINE");
            if (lineIds.Count == 0)
            {
                WriteMessage("Không có đối tượng nào được chọn.");
                return;
            }

            int count = 0;
            UsingTransaction(tr =>
            {
                foreach (ObjectId id in lineIds)
                {
                    DBObject obj = tr.GetObject(id, OpenMode.ForWrite);
                    XDataUtils.ClearWallData(obj, tr);
                    count++;
                }
            });

            WriteSuccess($"Đã xóa thông tin của {count} đối tượng.");
        }
    }
}