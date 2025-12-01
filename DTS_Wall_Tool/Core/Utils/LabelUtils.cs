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
    /// Tiện ích chuẩn bị nội dung Label cho việc hiển thị. 
    /// LabelUtils là "quản lý" - chuẩn bị nội dung, màu sắc. 
    /// LabelPlotter là "công nhân" - vẽ theo yêu cầu.
    /// </summary>
    public static class LabelUtils
    {
        private const double TEXT_HEIGHT_MAIN = 120.0;
        private const double TEXT_HEIGHT_SUB = 100.0;
        private const string LABEL_LAYER = "dts_frame_label";

        #region Main API

        /// <summary>
        /// Cập nhật nhãn cho Tường sau khi Sync/Mapping
        /// Format:
        ///   Dòng trên: [Handle] W200 DL=7. 20 kN/m
        ///   Dòng dưới: to B15 I=0.0to3.5 hoặc to B15 (full 9m)
        /// </summary>
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

            // Xác định màu theo trạng thái
            int statusColor = mapResult.GetColorIndex();

            // === DÒNG TRÊN: [Handle] W200 DL=7. 20 kN/m ===
            string handleText = FormatColor($"[{wallId.Handle}]", statusColor);
            string wallType = wData.WallType ?? $"W{wData.Thickness ?? 200:0}";
            string loadPattern = wData.LoadPattern ?? "DL";
            double loadValue = wData.LoadValue ?? 0;
            string loadText = $"{wallType} {loadPattern}={loadValue:0.00} kN/m";

            string topContent = $"{handleText} {{\\C7;{loadText}}}";

            // === DÒNG DƯỚI: to B15 I=0. 0to3.5 ===
            string botContent = GetMappingText(mapResult, wData.LoadPattern ?? "DL");

            // Lấy BlockTableRecord để vẽ
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(
                ent.Database.CurrentSpaceId, OpenMode.ForWrite);

            // Vẽ labels
            LabelPlotter.PlotLabel(btr, tr, pStart, pEnd, topContent,
                LabelPosition.MiddleTop, TEXT_HEIGHT_MAIN, LABEL_LAYER);

            LabelPlotter.PlotLabel(btr, tr, pStart, pEnd, botContent,
                LabelPosition.MiddleBottom, TEXT_HEIGHT_SUB, LABEL_LAYER);
        }

        /// <summary>
        /// Đổi màu entity theo trạng thái
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
        /// Bao gồm cả thông tin tải từ SAP nếu có
        /// </summary>
        public static string GetMappingText(MappingResult res, string loadPattern = "DL")
        {
            // Nếu chưa map
            if (!res.HasMapping)
                return FormatColor("to New", 1);

            // Nếu map nhiều dầm
            if (res.Mappings.Count > 1)
            {
                var names = res.Mappings.Select(m => m.TargetFrame).Distinct();
                return FormatColor("to " + string.Join(",", names), 3);
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

            // Thêm thông tin tải từ SAP nếu có
            if (SapUtils.IsConnected && map.TargetFrame != "New")
            {
                var sapLoads = SapUtils.GetFrameDistributedLoads(map.TargetFrame, loadPattern);
                if (sapLoads.Count > 0)
                {
                    double sapLoad = sapLoads.Sum(l => l.LoadValue);
                    result += $" [SAP:{sapLoad:0. 00}]";
                }
            }

            int color = res.GetColorIndex();
            return FormatColor(result, color);
        }

        /// <summary>
        /// Tạo label text với thông tin đầy đủ từ SAP
        /// </summary>
        public static string GetDetailedLabel(WallData wData, MappingResult mapResult)
        {
            var lines = new List<string>();

            // Line 1: Wall info
            string wallType = wData.WallType ?? $"W{wData.Thickness ?? 200:0}";
            lines.Add($"{wallType} T={wData.Thickness:0}mm H={wData.Height:0}mm");

            // Line 2: Load info
            if (wData.LoadValue.HasValue)
            {
                lines.Add($"{wData.LoadPattern}={wData.LoadValue:0.00} kN/m");
            }

            // Line 3: Mapping info
            if (mapResult.HasMapping)
            {
                var map = mapResult.Mappings.First();
                lines.Add($"-> {map.TargetFrame} ({map.MatchType})");
            }
            else
            {
                lines.Add("-> NEW");
            }

            return string.Join("\\P", lines); // \P = newline in MText
        }

        #endregion

        #region Sync Status Labels

        /// <summary>
        /// Tạo label hiển thị trạng thái đồng bộ
        /// </summary>
        public static string GetSyncStatusLabel(SyncState state, string details = null)
        {
            string statusText;
            int color;

            switch (state)
            {
                case SyncState.Synced:
                    statusText = "✓ Synced";
                    color = 3;
                    break;
                case SyncState.CadModified:
                    statusText = "↑ CAD Changed";
                    color = 2;
                    break;
                case SyncState.SapModified:
                    statusText = "↓ SAP Changed";
                    color = 5;
                    break;
                case SyncState.Conflict:
                    statusText = "⚠ Conflict";
                    color = 6;
                    break;
                case SyncState.SapDeleted:
                    statusText = "✗ SAP Deleted";
                    color = 1;
                    break;
                case SyncState.NewElement:
                    statusText = "● New";
                    color = 4;
                    break;
                default:
                    statusText = "?  Unknown";
                    color = 7;
                    break;
            }

            string result = FormatColor(statusText, color);
            if (!string.IsNullOrEmpty(details))
            {
                result += $" {details}";
            }

            return result;
        }

        #endregion
    }
}