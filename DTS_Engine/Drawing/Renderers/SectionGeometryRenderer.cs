using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DTS_Engine.Core.Data;
using DTS_Engine.Drawing.Models;
using DTS_Engine.Drawing.Utils;

namespace DTS_Engine.Drawing.Renderers
{
    /// <summary>
    /// Vẽ hình học một mặt cắt dầm (Bê tông, đai, thép chủ) vào một ô lưới.
    /// </summary>
    public class SectionGeometryRenderer
    {
        private readonly RebarLayoutSolver _rebarSolver;

        public SectionGeometryRenderer()
        {
            _rebarSolver = new RebarLayoutSolver();
        }

        /// <summary>
        /// Vẽ mặt cắt vào giữa ô được xác định bởi centerPoint.
        /// </summary>
        public void DrawSection(BlockTableRecord btr, SectionCellData data, Point3d centerPoint, DrawingSettings settings)
        {
            // 1. Tính toán khung bao bê tông (Căn giữa vào centerPoint)
            double halfW = data.Width / 2.0;
            double halfH = data.Height / 2.0;

            Extents3d concreteBounds = new Extents3d(
                new Point3d(centerPoint.X - halfW, centerPoint.Y - halfH, 0),
                new Point3d(centerPoint.X + halfW, centerPoint.Y + halfH, 0)
            );

            // 2. Vẽ viền bê tông (Layer Concrete)
            DrawConcreteOutline(btr, concreteBounds, settings);

            // 3. Vẽ thép đai (Layer Stirrup)
            if (data.Stirrup != null)
            {
                // Vẽ đai ngoài (Outer Stirrup)
                var stirrup = RebarPrimitiveUtils.CreateStirrup(concreteBounds, data.Cover, data.Stirrup.Diameter, settings.DrawStirrupHook, settings.ColorStirrup, settings.LayerStirrup);
                btr.AppendEntity(stirrup);

                // Vẽ đai trong (Inner Stirrup) nếu có 4 nhánh
                if (data.Stirrup.Legs == 4 && data.TopLayers != null && data.TopLayers.Count > 0 && data.TopLayers[0].Count >= 4)
                {
                    var layer = data.TopLayers[0];
                    double barRad = layer.Diameter / 2.0;
                    double edgeDist = data.Cover + data.Stirrup.Diameter + barRad;
                    double startX = concreteBounds.MinPoint.X + edgeDist;
                    double endX = concreteBounds.MaxPoint.X - edgeDist;
                    double spacing = (endX - startX) / (layer.Count - 1);

                    // Đai trong ôm từ thanh thứ 2 (index 1) đến thanh kế cuối (index Count-2)
                    double innerMinX = startX + spacing - barRad - data.Stirrup.Diameter;
                    double innerMaxX = startX + (layer.Count - 2) * spacing + barRad + data.Stirrup.Diameter;

                    var innerBounds = new Extents3d(
                        new Point3d(innerMinX + data.Stirrup.Diameter, concreteBounds.MinPoint.Y + data.Cover, 0),
                        new Point3d(innerMaxX - data.Stirrup.Diameter, concreteBounds.MaxPoint.Y - data.Cover, 0)
                    );

                    var innerStirrup = RebarPrimitiveUtils.CreateStirrup(innerBounds, 0, data.Stirrup.Diameter, false, settings.ColorStirrup, settings.LayerStirrup);
                    btr.AppendEntity(innerStirrup);
                }
            }

            // 4. Vẽ thép chủ (Layer MainRebar)
            _rebarSolver.DrawRebarGroup(btr, concreteBounds, data, true, settings.LayerMainRebar, settings.ColorMainRebar);  // Top
            _rebarSolver.DrawRebarGroup(btr, concreteBounds, data, false, settings.LayerMainRebar, settings.ColorMainRebar); // Bot

            // Vẽ thép Web (Side bars) - Dùng Layer và Color riêng
            _rebarSolver.DrawWebBars(btr, concreteBounds, data, settings.LayerSideBar, settings.ColorSideBar);

            // 5. Vẽ Dimension
            DrawDimensions(btr, concreteBounds, settings);
        }

        private void DrawDimensions(BlockTableRecord btr, Extents3d bounds, DrawingSettings settings)
        {
            double dimOffset = settings.TextHeight * 2.5; // Tăng khoảng cách để không đè chữ
            double textH = settings.TextHeight;

            // Dimension Ngang (B) - Phía dưới dầm
            var dimB = new RotatedDimension(
                0, // 0 độ = ngang
                new Point3d(bounds.MinPoint.X, bounds.MinPoint.Y, 0),
                new Point3d(bounds.MaxPoint.X, bounds.MinPoint.Y, 0),
                new Point3d(bounds.MinPoint.X, bounds.MinPoint.Y - dimOffset, 0),
                null,
                btr.Database.Dimstyle
            );

            // Dimension Dọc (H) - Bên trái dầm
            var dimH = new RotatedDimension(
                Math.PI / 2.0, // 90 độ = dọc
                new Point3d(bounds.MinPoint.X, bounds.MinPoint.Y, 0),
                new Point3d(bounds.MinPoint.X, bounds.MaxPoint.Y, 0),
                new Point3d(bounds.MinPoint.X - dimOffset, bounds.MinPoint.Y, 0),
                null,
                btr.Database.Dimstyle
            );

            // Gán các thuộc tính visual
            dimB.Dimscale = settings.DimScale;
            dimB.Dimtxt = textH; // Match exactly with other text
            dimB.Layer = settings.LayerDim;
            dimB.ColorIndex = settings.ColorDim;

            dimH.Dimscale = settings.DimScale;
            dimH.Dimtxt = textH;
            dimH.Layer = settings.LayerDim;
            dimH.ColorIndex = settings.ColorDim;

            btr.AppendEntity(dimB);
            btr.AppendEntity(dimH);
        }

        private void DrawConcreteOutline(BlockTableRecord btr, Extents3d bounds, DrawingSettings settings)
        {
            var pl = new Polyline(4);
            pl.AddVertexAt(0, new Point2d(bounds.MinPoint.X, bounds.MinPoint.Y), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(bounds.MaxPoint.X, bounds.MinPoint.Y), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(bounds.MaxPoint.X, bounds.MaxPoint.Y), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(bounds.MinPoint.X, bounds.MaxPoint.Y), 0, 0, 0);
            pl.Closed = true;
            pl.Layer = settings.LayerConcrete;
            pl.ColorIndex = settings.ColorConcrete;
            btr.AppendEntity(pl);
        }
    }
}
