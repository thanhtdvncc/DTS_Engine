using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using DTS_Wall_Tool.Core.Data;
using DTS_Wall_Tool.Core.Primitives;
using DTS_Wall_Tool.Core.Utils;

namespace DTS_Wall_Tool.Commands
{
    /// <summary>
    /// Các lệnh quản lý Origin (điểm gốc tầng)
    /// </summary>
    public class OriginCommands : CommandBase
    {
        /// <summary>
        /// Tạo/cập nhật origin marker
        /// </summary>
        [CommandMethod("DTS_SET_ORIGIN")]
        public void DTS_SET_ORIGIN()
        {
            WriteMessage("=== THIẾT LẬP ORIGIN ===");

            // Chọn Circle
            WriteMessage("Chọn CIRCLE làm origin marker.. .");
            var circleIds = AcadUtils.SelectObjectsOnScreen("CIRCLE");
            if (circleIds.Count != 1)
            {
                WriteError("Vui lòng chọn đúng 1 Circle.");
                return;
            }

            ObjectId circleId = circleIds[0];

            // Nhập tên tầng
            PromptStringOptions nameOpt = new PromptStringOptions("\nNhập tên tầng: ")
            {
                DefaultValue = "Tang1",
                AllowSpaces = true
            };

            PromptResult nameRes = Ed.GetString(nameOpt);
            if (nameRes.Status != PromptStatus.OK)
            {
                WriteMessage("Đã hủy.");
                return;
            }
            string storyName = nameRes.StringResult;

            // Nhập cao độ
            PromptDoubleOptions elevOpt = new PromptDoubleOptions("\nNhập cao độ (mm): ")
            {
                DefaultValue = 0,
                AllowNegative = true
            };

            PromptDoubleResult elevRes = Ed.GetDouble(elevOpt);
            double elevation = elevRes.Status == PromptStatus.OK ? elevRes.Value : 0;

            // Nhập chiều cao tầng
            PromptDoubleOptions heightOpt = new PromptDoubleOptions("\nNhập chiều cao tầng (mm): ")
            {
                DefaultValue = 3300,
                AllowNegative = false,
                AllowZero = false
            };

            PromptDoubleResult heightRes = Ed.GetDouble(heightOpt);
            double storyHeight = heightRes.Status == PromptStatus.OK ? heightRes.Value : 3300;

            // Lưu dữ liệu
            UsingTransaction(tr =>
            {
                DBObject circleObj = tr.GetObject(circleId, OpenMode.ForWrite);
                Circle circle = circleObj as Circle;

                StoryData storyData = new StoryData
                {
                    StoryName = storyName,
                    Elevation = elevation,
                    StoryHeight = storyHeight,
                    OffsetX = circle.Center.X,
                    OffsetY = circle.Center.Y
                };

                XDataUtils.WriteStoryData(circleObj, storyData, tr);

                // Đổi màu circle
                circle.ColorIndex = 6; // Magenta
            });

            WriteSuccess($"Đã tạo Origin [{circleId.Handle}]: {storyName}, Z={elevation}, H={storyHeight}");
        }

        /// <summary>
        /// Xem thông tin origin
        /// </summary>
        [CommandMethod("DTS_SHOW_ORIGIN")]
        public void DTS_SHOW_ORIGIN()
        {
            WriteMessage("=== THÔNG TIN ORIGIN ===");

            var circleIds = AcadUtils.SelectAll("CIRCLE");
            int foundCount = 0;

            UsingTransaction(tr =>
            {
                foreach (ObjectId circleId in circleIds)
                {
                    DBObject circleObj = tr.GetObject(circleId, OpenMode.ForRead);
                    StoryData storyData = XDataUtils.ReadStoryData(circleObj);

                    if (storyData != null)
                    {
                        WriteMessage($"  [{circleId.Handle}]: {storyData}");
                        foundCount++;
                    }
                }
            });

            WriteMessage($"Tìm thấy {foundCount} Origin markers.");
        }
    }
}