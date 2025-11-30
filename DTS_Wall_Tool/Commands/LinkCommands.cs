using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using DTS_Wall_Tool.Core.Data;
using DTS_Wall_Tool.Core.Primitives;
using DTS_Wall_Tool.Core.Utils;

namespace DTS_Wall_Tool.Commands
{
    /// <summary>
    /// Các lệnh liên kết tường với origin
    /// </summary>
    public class LinkCommands : CommandBase
    {
        private const string LINK_LAYER = "dts_linkmap";
        private const string HIGHLIGHT_LAYER = "dts_highlight";

        /// <summary>
        /// Liên kết tường với origin marker
        /// </summary>
        [CommandMethod("DTS_LINK")]
        public void DTS_LINK()
        {
            WriteMessage("=== LIÊN KẾT TƯỜNG VỚI ORIGIN ===");

            // Chọn origin (Circle)
            WriteMessage("Chọn điểm ORIGIN (Circle).. .");
            var originIds = AcadUtils.SelectObjectsOnScreen("CIRCLE");
            if (originIds.Count != 1)
            {
                WriteError("Vui lòng chọn đúng 1 Circle làm origin.");
                return;
            }
            ObjectId originId = originIds[0];
            string originHandle = originId.Handle.ToString();

            // Chọn các tường
            WriteMessage("Chọn các LINE tường cần liên kết...");
            var lineIds = AcadUtils.SelectObjectsOnScreen("LINE");
            if (lineIds.Count == 0)
            {
                WriteMessage("Không có tường nào được chọn.");
                return;
            }

            int linked = 0;
            UsingTransaction(tr =>
            {
                // Chuẩn bị layer
                AcadUtils.CreateLayer(LINK_LAYER, 3);

                // Lấy tâm origin
                Circle originCircle = tr.GetObject(originId, OpenMode.ForRead) as Circle;
                Point2D originCenter = new Point2D(originCircle.Center.X, originCircle.Center.Y);

                // Liên kết từng tường
                foreach (ObjectId lineId in lineIds)
                {
                    DBObject lineObj = tr.GetObject(lineId, OpenMode.ForWrite);
                    Line lineEnt = lineObj as Line;

                    // Đọc/tạo WallData
                    WallData wData = XDataUtils.ReadWallData(lineObj) ?? new WallData();
                    wData.OriginHandle = originHandle;
                    XDataUtils.SaveWallData(lineObj, wData, tr);

                    // Vẽ đường liên kết
                    Point2D lineCenter = AcadUtils.GetEntityCenter(lineEnt);
                    AcadUtils.CreateLine(originCenter, lineCenter, LINK_LAYER, 3, tr);

                    linked++;
                }

                // Cập nhật danh sách con cho origin
                DBObject originObj = tr.GetObject(originId, OpenMode.ForWrite);
                var originData = XDataUtils.ReadStoryData(originObj) ?? new StoryData { StoryName = "Story" };

                var updates = new Dictionary<string, object>();
                updates["xType"] = "STORY_ORIGIN";
                updates["xStoryName"] = originData.StoryName;
                updates["xElevation"] = originData.Elevation;

                var childHandles = new List<string>();
                foreach (var lineId in lineIds)
                {
                    childHandles.Add(lineId.Handle.ToString());
                }
                updates["xChildHandles"] = childHandles;

                XDataUtils.UpdateData(originObj, updates, tr);
            });

            WriteSuccess($"Đã liên kết {linked} tường với Origin [{originHandle}].");
        }

        /// <summary>
        /// Hiển thị các liên kết
        /// </summary>
        [CommandMethod("DTS_SHOW_LINK")]
        public void DTS_SHOW_LINK()
        {
            WriteMessage("=== HIỂN THỊ LIÊN KẾT ===");

            // Xóa highlight cũ
            AcadUtils.ClearLayer(LINK_LAYER);
            AcadUtils.ClearLayer(HIGHLIGHT_LAYER);

            var lineIds = AcadUtils.SelectAll("LINE");
            int linkedCount = 0;

            UsingTransaction(tr =>
            {
                AcadUtils.CreateLayer(LINK_LAYER, 3);
                AcadUtils.CreateLayer(HIGHLIGHT_LAYER, 1);

                foreach (ObjectId lineId in lineIds)
                {
                    DBObject lineObj = tr.GetObject(lineId, OpenMode.ForRead);
                    WallData wData = XDataUtils.ReadWallData(lineObj);

                    if (wData != null && !string.IsNullOrEmpty(wData.OriginHandle))
                    {
                        ObjectId originId = AcadUtils.GetObjectIdFromHandle(wData.OriginHandle);
                        if (originId != ObjectId.Null)
                        {
                            Entity originEnt = tr.GetObject(originId, OpenMode.ForRead) as Entity;
                            Entity lineEnt = lineObj as Entity;

                            if (originEnt != null && lineEnt != null)
                            {
                                Point2D originCenter = AcadUtils.GetEntityCenter(originEnt);
                                Point2D lineCenter = AcadUtils.GetEntityCenter(lineEnt);

                                AcadUtils.CreateLine(originCenter, lineCenter, LINK_LAYER, 3, tr);
                                linkedCount++;
                            }
                        }
                    }
                }
            });

            WriteMessage($"Tìm thấy {linkedCount} tường có liên kết.");
        }

        /// <summary>
        /// Xóa liên kết
        /// </summary>
        [CommandMethod("DTS_BREAK_LINK")]
        public void DTS_BREAK_LINK()
        {
            WriteMessage("=== XÓA LIÊN KẾT ===");

            var lineIds = AcadUtils.SelectObjectsOnScreen("LINE");
            if (lineIds.Count == 0)
            {
                WriteMessage("Không có đối tượng nào được chọn.");
                return;
            }

            int broken = 0;
            UsingTransaction(tr =>
            {
                foreach (ObjectId lineId in lineIds)
                {
                    DBObject lineObj = tr.GetObject(lineId, OpenMode.ForWrite);
                    WallData wData = XDataUtils.ReadWallData(lineObj);

                    if (wData != null && !string.IsNullOrEmpty(wData.OriginHandle))
                    {
                        wData.OriginHandle = null;
                        XDataUtils.SaveWallData(lineObj, wData, tr);
                        broken++;
                    }
                }
            });

            // Xóa visual
            AcadUtils.ClearLayer(LINK_LAYER);

            WriteSuccess($"Đã xóa liên kết của {broken} tường.");
        }

        /// <summary>
        /// Xóa tất cả visual links
        /// </summary>
        [CommandMethod("DTS_CLEAR_LINK")]
        public void DTS_CLEAR_LINK()
        {
            AcadUtils.ClearLayer(LINK_LAYER);
            AcadUtils.ClearLayer(HIGHLIGHT_LAYER);
            WriteSuccess("Đã xóa tất cả đường liên kết hiển thị.");
        }
    }
}