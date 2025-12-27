using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System;

namespace DTS_Engine.Drawing.Utils
{
    /// <summary>
    /// Các hàm tiện ích vẽ thép cơ bản (đai, chấm thép) cho AutoCAD.
    /// </summary>
    public static class RebarPrimitiveUtils
    {
        public static Polyline CreateStirrup(Extents3d concreteBounds, double cover, double barDia, bool drawHook = true, int colorIndex = 1, string layer = "0")
        {
            var pl = new Polyline();

            // 1. Tính khung đai (Offset vào trong)
            double minX = concreteBounds.MinPoint.X + cover;
            double minY = concreteBounds.MinPoint.Y + cover;
            double maxX = concreteBounds.MaxPoint.X - cover;
            double maxY = concreteBounds.MaxPoint.Y - cover;

            if (drawHook)
            {
                // 2. Thông số móc (Hook) - Chuẩn TCVN/ACI: 6db hoặc min 75mm
                double hookLen = Math.Max(6 * barDia, 75.0);
                double hookDelta = hookLen / Math.Sqrt(2); // Chiếu lên trục X/Y cho góc 135 độ

                // 3. Vẽ Polyline (Bắt đầu từ Móc 1 -> Chạy vòng quanh -> Móc 2)
                pl.AddVertexAt(0, new Point2d(minX + hookDelta, maxY - hookDelta), 0, 0, 0); // Móc 1
                pl.AddVertexAt(1, new Point2d(minX, maxY), 0, 0, 0); // Góc 1
                pl.AddVertexAt(2, new Point2d(minX, minY), 0, 0, 0); // Góc 2
                pl.AddVertexAt(3, new Point2d(maxX, minY), 0, 0, 0); // Góc 3
                pl.AddVertexAt(4, new Point2d(maxX, maxY), 0, 0, 0); // Góc 4
                pl.AddVertexAt(5, new Point2d(minX, maxY), 0, 0, 0); // Về lại Góc 1

                double offsetHook2 = 10.0;
                pl.AddVertexAt(6, new Point2d(minX + hookDelta + offsetHook2, maxY - hookDelta - offsetHook2), 0, 0, 0); // Móc 2
            }
            else
            {
                // Vẽ khép kín đơn giản
                pl.AddVertexAt(0, new Point2d(minX, maxY), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(minX, minY), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(maxX, minY), 0, 0, 0);
                pl.AddVertexAt(3, new Point2d(maxX, maxY), 0, 0, 0);
                pl.Closed = true;
            }

            pl.Layer = layer;
            pl.ColorIndex = colorIndex;
            return pl;
        }

        /// <summary>
        /// Vẽ chấm thép (Donut) bằng Polyline có độ dày.
        /// Cơ chế: Polyline có Width = W tạo nét vẽ dôi ra W/2 mỗi bên.
        /// Để có đường kính Outside = D, đường dẫn phải có bán kính R_path = D/4 và Width = D/2.
        /// </summary>
        public static Polyline CreateRebarDot(Point3d center, double diameter, int colorIndex = 3, string layer = "0")
        {
            var pl = new Polyline();
            double pathRadius = diameter / 4.0;
            double width = diameter / 2.0;

            // Donut được tạo bởi 2 cung tròn (Bulge = 1.0) khép kín
            // Vertex 0: Bên trái tâm đường dẫn
            pl.AddVertexAt(0, new Point2d(center.X - pathRadius, center.Y), 1.0, width, width);
            // Vertex 1: Bên phải tâm đường dẫn
            pl.AddVertexAt(1, new Point2d(center.X + pathRadius, center.Y), 1.0, width, width);

            pl.Closed = true;
            pl.Layer = layer;
            pl.ColorIndex = colorIndex;

            return pl;
        }
    }
}
