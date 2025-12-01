using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using DTS_Wall_Tool.Core.Data;
using DTS_Wall_Tool.Core.Engines;
using DTS_Wall_Tool.Core.Primitives;

namespace DTS_Wall_Tool.Core.Utils
{
    /// <summary>
    /// Tiện ích chuẩn bị nội dung Label cho việc hiển thị
    /// LabelUtils là "quản lý" - chuẩn bị nội dung, màu sắc, quyết định vị trí
    /// LabelPlotter là "công nhân" - chỉ vẽ theo yêu cầu
    /// </summary>
    public static class LabelUtils
    {
        // Cấu hình hiển thị
        private const double TEXT_HEIGHT_MAIN = 150.0;
        private const double TEXT_HEIGHT_SUB = 120.0;
        private const string LABEL_LAYER = "dts_frame_label";

        #region Main API

        /// <summary>
        /// Cập nhật nhãn cho Tường sau khi Sync/Mapping
        /// </summary>
        /// <param name="wallId">ObjectId của tường</param>
        /// <param name="wData">Dữ liệu tường</param>
        /// <param name="mapResult">Kết quả mapping</param>
        /// <param name="tr">Transaction hiện tại</param>
        public static void UpdateWallLabels(ObjectId wallId, WallData wData, MappingResult mapResult, Transaction tr)
        {
            // 1. Lấy entity và kiểm tra
            Entity ent = tr.GetObject(wallId, OpenMode.ForRead) as Entity;
            if (ent == null) return;

            Point2D pStart, pEnd;
            if (ent is Line line)
            {
                pStart = new Point2D(line.StartPoint.X, line.StartPoint.Y);
                pEnd = new Point2D(line.EndPoint.X, line.EndPoint.Y);
            }
            else return;

            // 2. Chuẩn bị nội dung Text
            // Màu sắc cho Handle: Xanh (3) nếu Full, Vàng (2) nếu Partial, Đỏ (1) nếu New
            int statusColor = mapResult.GetColorIndex();

            // --- Dòng trên (Middle Top): [Handle] W200 DL=10. 5 kN/m2 ---
            string handleText = FormatColor($"[{wallId.Handle}]", statusColor);
            string wallType = wData.WallType ?? $"W{wData.Thickness ?? 200:0}";
            string loadPattern = wData.LoadPattern ?? "DL";
            double loadValue = wData.LoadValue ?? 0;
            string infoText = $"{wallType} {loadPattern}={loadValue:0.00} kN/m2";
            string topContent = $"{handleText} {{\\C7;{infoText}}}";

            // --- Dòng dưới (Middle Bottom): to B15 I=0.0to3.5 ---
            string botContent = GetMappingText(mapResult);

            // 3. Lấy BlockTableRecord để vẽ
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(ent.Database.CurrentSpaceId, OpenMode.ForWrite);

            // 4.  Vẽ (Gọi LabelPlotter)
            // Dòng trên
            LabelPlotter.PlotLabel(btr, tr, pStart, pEnd, topContent, LabelPosition.MiddleTop, TEXT_HEIGHT_MAIN, LABEL_LAYER);

            // Dòng dưới (Nhỏ hơn một chút cho đẹp)
            LabelPlotter.PlotLabel(btr, tr, pStart, pEnd, botContent, LabelPosition.MiddleBottom, TEXT_HEIGHT_SUB, LABEL_LAYER);
        }

        /// <summary>
        /// Đổi màu entity theo trạng thái mapping
        /// </summary>
        public static void SetEntityColor(ObjectId id, int colorIndex, Transaction tr)
        {
            Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
            if (ent != null) ent.ColorIndex = colorIndex;
        }

        #endregion

        #region Content Formatting

        /// <summary>
        /// Format chuỗi với màu MText
        /// </summary>
        public static string FormatColor(string text, int colorIndex)
        {
            return $"{{\\C{colorIndex};{text}}}";
        }

        /// <summary>
        /// Tạo nội dung text cho phần mapping
        /// </summary>
        public static string GetMappingText(MappingResult res)
        {
            // Nếu chưa map
            if (!res.HasMapping)
                return FormatColor("to New", 1); // Màu đỏ

            // Nếu map nhiều dầm
            if (res.Mappings.Count > 1)
            {
                var names = res.Mappings.Select(m => m.TargetFrame).Distinct();
                return FormatColor("to " + string.Join(",", names), 3); // Màu xanh
            }

            // Map 1 dầm
            var map = res.Mappings[0];
            if (map.TargetFrame == "New")
                return FormatColor("to New", 1);

            string result = $"to {map.TargetFrame}";

            if (map.MatchType == "FULL" || map.MatchType == "EXACT")
            {
                result += $" (full {map.CoveredLength / 1000.0:0.#}m)";
            }
            else
            {
                double i = map.DistI / 1000.0;
                double j = map.DistJ / 1000.0;
                result += $" I={i:0. 0}to{j:0.0}";
            }

            // Màu theo trạng thái
            int color = res.GetColorIndex();
            return FormatColor(result, color);
        }

        /// <summary>
        /// Tạo nội dung cho dòng trên (thông tin tường)
        /// </summary>
        public static string GetTopLabelContent(ObjectId wallId, WallData wData, int statusColor)
        {
            string handleText = FormatColor($"[{wallId.Handle}]", statusColor);
            string wallType = wData.WallType ?? $"W{wData.Thickness ?? 200:0}";
            string loadPattern = wData.LoadPattern ?? "DL";
            double loadValue = wData.LoadValue ?? 0;
            string infoText = $"{wallType} {loadPattern}={loadValue:0. 00} kN/m2";
            return $"{handleText} {{\\C7;{infoText}}}";
        }

        #endregion
    }
}