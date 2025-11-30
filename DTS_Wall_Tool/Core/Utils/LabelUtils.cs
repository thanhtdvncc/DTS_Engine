using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DTS_Wall_Tool.Core.Data;
using DTS_Wall_Tool.Core.Primitives;

namespace DTS_Wall_Tool.Core.Utils
{
    /// <summary>
    /// Tiện ích quản lý label trong AutoCAD
    /// </summary>
    public static class LabelUtils
    {
        private const string LABEL_LAYER = "dts_labels";
        private const double DEFAULT_TEXT_HEIGHT = 200;

        /// <summary>
        /// Tạo/cập nhật label cho tường
        /// </summary>
        public static void UpdateWallLabel(ObjectId wallId, WallData wData, Transaction tr)
        {
            // Nội dung label
            string content = BuildLabelContent(wallId, wData);

            // Vị trí (tâm tường)
            Entity ent = tr.GetObject(wallId, OpenMode.ForRead) as Entity;
            Point2D center = AcadUtils.GetEntityCenter(ent);

            // Đảm bảo layer tồn tại
            AcadUtils.CreateLayer(LABEL_LAYER, 2); // Màu vàng

            // Vẽ MText
            AcadUtils.CreateMText(center, content, DEFAULT_TEXT_HEIGHT, LABEL_LAYER, 2, tr);
        }

        /// <summary>
        /// Tạo nội dung label từ WallData
        /// </summary>
        public static string BuildLabelContent(ObjectId wallId, WallData wData)
        {
            string handleStr = wallId.Handle.ToString();
            string wallType = wData.WallType ?? "? ";
            string loadPattern = wData.LoadPattern ?? "DL";
            double loadValue = wData.LoadValue ?? 0;

            string content = $"[{handleStr}] {wallType} {loadPattern}={loadValue:0.00}kN/m";

            // Thêm thông tin mapping
            if (wData.Mappings != null && wData.Mappings.Count > 0)
            {
                foreach (var map in wData.Mappings)
                {
                    content += $"\n-> {map.TargetFrame}";
                }
            }

            return content;
        }

        /// <summary>
        /// Tạo label cho mapping result
        /// </summary>
        public static void CreateMappingLabel(Point2D position, string frameName, string mapType, Transaction tr)
        {
            string content = $"{frameName} [{mapType}]";
            AcadUtils.CreateLayer(LABEL_LAYER, 3); // Màu xanh lá
            AcadUtils.CreateMText(position, content, DEFAULT_TEXT_HEIGHT * 0.8, LABEL_LAYER, 3, tr);
        }

        /// <summary>
        /// Xóa tất cả labels
        /// </summary>
        public static void ClearAllLabels()
        {
            AcadUtils.ClearLayer(LABEL_LAYER);
        }

        /// <summary>
        /// Đổi màu entity theo trạng thái mapping
        /// </summary>
        public static void SetEntityColorByStatus(ObjectId id, bool hasMapping, Transaction tr)
        {
            // 3 = Xanh lá (có mapping), 1 = Đỏ (không có mapping)
            int colorIndex = hasMapping ? 3 : 1;
            AcadUtils.SetEntityColor(id, colorIndex, tr);
        }

        /// <summary>
        /// Đổi màu entity
        /// </summary>
        public static void SetEntityColor(ObjectId id, int colorIndex, Transaction tr)
        {
            AcadUtils.SetEntityColor(id, colorIndex, tr);
        }
    }
}