using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DTS_Engine.Drawing.Models;
using DTS_Engine.Drawing.Utils;
using System.Collections.Generic;
using System.Linq;

namespace DTS_Engine.Drawing.Renderers
{
    /// <summary>
    /// Bộ xử lý rải thép (LayoutSolver) cho mặt cắt dầm.
    /// Quyết định vị trí của từng thanh thép dựa trên lớp và quy định cấu tạo.
    /// </summary>
    public class RebarLayoutSolver
    {
        /// <summary>
        /// Vẽ nhóm thép (Top hoặc Bottom) vào mặt cắt.
        /// </summary>
        /// <param name="btr">ModelSpace record</param>
        /// <param name="concreteBounds">Khung bao bê tông</param>
        /// <param name="data">Dữ liệu mặt cắt</param>
        /// <param name="isTop">True nếu vẽ thép trên, False nếu thép dưới</param>
        /// <param name="layerName">Tên layer AutoCAD</param>
        /// <param name="colorIndex">Màu AutoCAD</param>
        public void DrawRebarGroup(
            BlockTableRecord btr,
            Extents3d concreteBounds,
            SectionCellData data,
            bool isTop,
            string layerName = "0",
            int colorIndex = 3)
        {
            var layers = isTop ? data.TopLayers : data.BotLayers;
            if (layers == null || layers.Count == 0) return;

            double cover = data.Cover;
            double stirrupDia = data.Stirrup?.Diameter ?? 8.0;

            // Khoảng cách thông thủy giữa các lớp thép (mm)
            // TCVN: min 25mm hoặc d_max
            double layerGap = 35.0;

            // Xác định tọa độ Y bắt đầu (Mép sát đai)
            double currentY;
            if (isTop)
                currentY = concreteBounds.MaxPoint.Y - cover - stirrupDia; // Đi xuống
            else
                currentY = concreteBounds.MinPoint.Y + cover + stirrupDia; // Đi lên

            foreach (var layer in layers)
            {
                if (layer.Count <= 0) continue;

                double barRad = layer.Diameter / 2.0;

                // Điều chỉnh Y về đúng tâm thanh thép
                double rowY = isTop ? (currentY - barRad) : (currentY + barRad);

                // Tính toán rải ngang (Horizontal Distribution)
                // Khoảng cách từ mép bê tông đến tâm thanh ngoài cùng
                double edgeDist = cover + stirrupDia + barRad;

                double startX = concreteBounds.MinPoint.X + edgeDist;
                double endX = concreteBounds.MaxPoint.X - edgeDist;
                double widthAvail = endX - startX;

                if (layer.Count == 1)
                {
                    // 1 thanh thì vẽ chính giữa
                    Point3d pos = new Point3d((startX + endX) / 2.0, rowY, 0);
                    btr.AppendEntity(RebarPrimitiveUtils.CreateRebarDot(pos, layer.Diameter, colorIndex, layerName));
                }
                else
                {
                    // Nhiều thanh thì chia đều
                    double spacing = widthAvail / (layer.Count - 1);
                    for (int i = 0; i < layer.Count; i++)
                    {
                        double x = startX + i * spacing;
                        Point3d pos = new Point3d(x, rowY, 0);
                        btr.AppendEntity(RebarPrimitiveUtils.CreateRebarDot(pos, layer.Diameter, colorIndex, layerName));
                    }
                }

                // Cập nhật Y cho lớp tiếp theo
                if (isTop)
                    currentY = currentY - layer.Diameter - layerGap;
                else
                    currentY = currentY + layer.Diameter + layerGap;
            }
        }

        public void DrawWebBars(
            BlockTableRecord btr,
            Extents3d concreteBounds,
            SectionCellData data,
            string layerName = "0",
            int colorIndex = 3)
        {
            var layers = ParseWebLayers(data.WebText);
            if (layers == null || layers.Count == 0) return;

            double cover = data.Cover;
            double stirrupDia = data.Stirrup?.Diameter ?? 8.0;
            double layerGap = 30.0; // Khoảng cách giữa các lớp thép

            // Tính toán vùng chiều cao đã bị chiếm bởi thép TOP
            double topOccupied = cover + stirrupDia;
            if (data.TopLayers != null)
            {
                foreach (var l in data.TopLayers)
                    topOccupied += l.Diameter + layerGap;
            }

            // Tính toán vùng chiều cao đã bị chiếm bởi thép BOT
            double botOccupied = cover + stirrupDia;
            if (data.BotLayers != null)
            {
                foreach (var l in data.BotLayers)
                    botOccupied += l.Diameter + layerGap;
            }

            // Vùng Web là phần còn lại ở giữa
            double topLimit = concreteBounds.MaxPoint.Y - topOccupied;
            double botLimit = concreteBounds.MinPoint.Y + botOccupied;
            double netHeight = topLimit - botLimit;

            if (netHeight <= 0) return; // Không đủ chỗ rải Web

            foreach (var layer in layers)
            {
                if (layer.Count < 2) continue; // Web bar thường đi theo cặp 2 bên sườn

                int pairs = layer.Count / 2;
                // Chia đều trong netHeight
                double spacingY = netHeight / (pairs + 1);

                for (int i = 1; i <= pairs; i++)
                {
                    double y = topLimit - i * spacingY;
                    double barRad = layer.Diameter / 2.0;

                    // Thanh bên trái sườn (Tâm nằm sát mặt trong đai)
                    Point3d posL = new Point3d(concreteBounds.MinPoint.X + cover + stirrupDia + barRad, y, 0);
                    // Thanh bên phải sườn
                    Point3d posR = new Point3d(concreteBounds.MaxPoint.X - cover - stirrupDia - barRad, y, 0);

                    btr.AppendEntity(RebarPrimitiveUtils.CreateRebarDot(posL, layer.Diameter, colorIndex, layerName));
                    btr.AppendEntity(RebarPrimitiveUtils.CreateRebarDot(posR, layer.Diameter, colorIndex, layerName));
                }
            }
        }

        private List<RebarLayer> ParseWebLayers(string webText)
        {
            if (string.IsNullOrEmpty(webText) || webText == "-" || webText == "0") return null;
            var details = DTS_Engine.Core.Utils.RebarStringParser.GetDetails(webText);
            return details.Select(d => new RebarLayer { Count = d.Count, Diameter = d.Diameter }).ToList();
        }
    }
}
