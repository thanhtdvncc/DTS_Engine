using Autodesk.AutoCAD.DatabaseServices;
using DTS_Wall_Tool.Core.Data;
using DTS_Wall_Tool.Core.Engines;
using DTS_Wall_Tool.Core.Primitives;
using System;
using System.Linq;

namespace DTS_Wall_Tool.Core.Utils
{
    public static class LabelUtils
    {
        private const double TEXT_HEIGHT_MAIN = 150.0;
        private const double TEXT_HEIGHT_SUB = 120.0;
        private const string LABEL_LAYER = "dts_frame_label";

        public static void UpdateWallLabels(ObjectId wallId, WallData wData, MappingResult mapResult, Transaction tr)
        {
            Entity ent = tr.GetObject(wallId, OpenMode.ForRead) as Entity;
            if (ent == null) return;

            Point2D pStart, pEnd;
            if (ent is Line line)
            {
                pStart = new Point2D(line.StartPoint.X, line.StartPoint.Y);
                pEnd = new Point2D(line.EndPoint.X, line.EndPoint.Y);
            }
            else return;

            int statusColor = mapResult.GetColorIndex();

            // --- XỬ LÝ DÒNG TRÊN (INFO) ---
            string topContent;
            string handleText = FormatColor($"[{wallId.Handle}]", statusColor);

            // Kiểm tra xem có dữ liệu tải trọng chưa
            if (wData.Thickness.HasValue || wData.LoadValue.HasValue)
            {
                // Đã có data: Hiển thị đầy đủ
                // VD: [Handle] W220 DL=12.50
                string wallType = wData.WallType ?? $"W{wData.Thickness ?? 0}";
                string loadInfo = $"{wallType} {wData.LoadPattern ?? "DL"}={wData.LoadValue ?? 0:0.00}";
                topContent = $"{handleText} {FormatColor(loadInfo, 7)}";
            }
            else
            {
                // Chưa có data: Chỉ hiển thị Handle (cho gọn bản vẽ lúc Mapping)
                // VD: [Handle]
                topContent = handleText;
            }

            // --- XỬ LÝ DÒNG DƯỚI (MAPPING) ---
            string botContent = GetMappingText(mapResult);

            // --- VẼ ---
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(ent.Database.CurrentSpaceId, OpenMode.ForWrite);
            LabelPlotter.PlotLabel(btr, tr, pStart, pEnd, topContent, LabelPosition.MiddleTop, TEXT_HEIGHT_MAIN, LABEL_LAYER);
            LabelPlotter.PlotLabel(btr, tr, pStart, pEnd, botContent, LabelPosition.MiddleBottom, TEXT_HEIGHT_SUB, LABEL_LAYER);
        }

        // ... (Các hàm FormatColor, GetMappingText giữ nguyên như lần trước) ...
        public static string FormatColor(string text, int colorIndex)
        {
            return $"{{\\C{colorIndex};{text}}}";
        }

        public static string GetMappingText(MappingResult res)
        {
            if (!res.HasMapping) return FormatColor("to New", 1);

            if (res.Mappings.Count > 1)
            {
                var names = System.Linq.Enumerable.Select(res.Mappings, m => m.TargetFrame).Distinct();
                return FormatColor("to " + string.Join(",", names), 2);
            }

            var map = res.Mappings[0];
            if (map.TargetFrame == "New") return FormatColor("to New", 1);

            string result = $"to {map.TargetFrame}";
            if (map.MatchType == "FULL" || map.MatchType == "EXACT")
                result += $" (full)"; // Rút gọn cho đỡ rối
            else
            {
                double i = map.DistI / 1000.0;
                double j = map.DistJ / 1000.0;
                result += $" I={i:0.0}-{j:0.0}"; // Rút gọn format
            }

            return FormatColor(result, res.GetColorIndex());
        }
    }
}