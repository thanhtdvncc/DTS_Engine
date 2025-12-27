using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DTS_Engine.Core.Data;
using DTS_Engine.Drawing.Models;
using System.Collections.Generic;

namespace DTS_Engine.Drawing.Renderers
{
    /// <summary>
    /// Vẽ một khối dầm (Beam Block) hoàn chỉnh trong bảng.
    /// Bao gồm cột MARK/SIZE gộp và các hàng END/CENTER.
    /// </summary>
    public class BeamBlockRenderer
    {
        private readonly SectionGeometryRenderer _sectionRenderer;
        private readonly TableLayoutConfig _config;

        public BeamBlockRenderer(TableLayoutConfig config)
        {
            _config = config;
            _sectionRenderer = new SectionGeometryRenderer();
        }

        /// <summary>
        /// Vẽ khối dầm và trả về tổng chiều cao của khối.
        /// </summary>
        /// <param name="origin">Góc trên-trái của khối dầm trong bảng</param>
        public double DrawBeamBlock(BlockTableRecord btr, Point3d origin, BeamScheduleRowData beam, DrawingSettings settings)
        {
            int numSections = beam.Cells.Count;
            if (numSections == 0) return 0;

            double totalBlockHeight = numSections * _config.TotalRowHeight;

            // 1. Vẽ ô MARK gộp (Merged Cell)
            DrawMergedMarkCell(btr, origin, beam.BeamName, beam.SizeLabel, totalBlockHeight, settings);

            // 2. Duyệt qua từng ô mặt cắt (END / CENTER)
            for (int i = 0; i < numSections; i++)
            {
                // Tọa độ Y của hàng hiện tại
                double rowY = origin.Y - (i * _config.TotalRowHeight);
                Point3d rowOrigin = new Point3d(origin.X + _config.ColWidth_Mark, rowY, 0);

                DrawSectionRow(btr, rowOrigin, beam.Cells[i], settings);
            }

            // 3. Vẽ khung ngoài cho toàn bộ Block dầm (Nếu cần phân cách)
            DrawBlockBorder(btr, origin, totalBlockHeight, settings);

            return totalBlockHeight;
        }

        private void DrawMergedMarkCell(BlockTableRecord btr, Point3d origin, string name, string size, double height, DrawingSettings settings)
        {
            // Điểm giữa của ô gộp
            Point3d center = new Point3d(
                origin.X + (_config.ColWidth_Mark / 2.0),
                origin.Y - (height / 2.0),
                0
            );

            // Vẽ khung ô MARK
            DrawCellBorder(btr, origin, _config.ColWidth_Mark, height, settings);

            // Điền chữ MARK (Tên dầm + Kích thước)
            var mtext = new MText();
            mtext.Contents = $"{name}\\P({size})";
            mtext.Location = center;
            mtext.Attachment = AttachmentPoint.MiddleCenter;
            mtext.TextHeight = settings.TextHeight;
            mtext.Layer = settings.LayerText;
            mtext.ColorIndex = settings.ColorText;

            btr.AppendEntity(mtext);
        }

        private void DrawSectionRow(BlockTableRecord btr, Point3d rowOrigin, SectionCellData data, DrawingSettings settings)
        {
            double rowH = _config.TotalRowHeight;
            double xLoc = rowOrigin.X;
            double xSection = xLoc + _config.ColWidth_Loc;
            double xTop = xSection + _config.ColWidth_Section;
            double xBot = xTop + _config.ColWidth_Rebar;
            double xStirrup = xBot + _config.ColWidth_Rebar;
            double xWeb = xStirrup + _config.ColWidth_Stirrup;

            // 1. Ô LOCATION (Cập nhật chiều cao đồng bộ)
            DrawMTextCell(btr, new Point3d(xLoc, rowOrigin.Y, 0), _config.ColWidth_Loc, rowH, data.LocationName, settings);

            // 2. Ô SECTION (Vẽ trong ô có chiều cao đồng bộ)
            DrawCellBorder(btr, new Point3d(xSection, rowOrigin.Y, 0), _config.ColWidth_Section, rowH, settings);

            // Căn giữa hình vẽ mặt cắt trong ô lớn
            Point3d sectionCenter = new Point3d(
                xSection + (_config.ColWidth_Section / 2.0),
                rowOrigin.Y - (rowH / 2.0),
                0
            );
            _sectionRenderer.DrawSection(btr, data, sectionCenter, settings);

            // 3. Các ô TEXT (TOP, BOT, STIRRUP, WEB)
            DrawMTextCell(btr, new Point3d(xTop, rowOrigin.Y, 0), _config.ColWidth_Rebar, rowH, data.TopText, settings);
            DrawMTextCell(btr, new Point3d(xBot, rowOrigin.Y, 0), _config.ColWidth_Rebar, rowH, data.BotText, settings);
            DrawMTextCell(btr, new Point3d(xStirrup, rowOrigin.Y, 0), _config.ColWidth_Stirrup, rowH, data.StirrupText, settings);
            DrawMTextCell(btr, new Point3d(xWeb, rowOrigin.Y, 0), _config.ColWidth_Web, rowH, data.WebText, settings);
        }

        private void DrawCellBorder(BlockTableRecord btr, Point3d topLeft, double width, double height, DrawingSettings settings)
        {
            var pl = new Polyline(4);
            pl.AddVertexAt(0, new Point2d(topLeft.X, topLeft.Y), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(topLeft.X + width, topLeft.Y), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(topLeft.X + width, topLeft.Y - height), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(topLeft.X, topLeft.Y - height), 0, 0, 0);
            pl.Closed = true;
            pl.Layer = settings.LayerDim;
            pl.ColorIndex = settings.ColorDim;
            btr.AppendEntity(pl);
        }

        private void DrawTextCell(BlockTableRecord btr, Point3d topLeft, double width, double height, string text, DrawingSettings settings)
        {
            DrawCellBorder(btr, topLeft, width, height, settings);
            if (string.IsNullOrEmpty(text)) return;

            var mtext = new MText();
            mtext.Contents = text;
            mtext.Location = new Point3d(topLeft.X + width / 2.0, topLeft.Y - height / 2.0, 0);
            mtext.Attachment = AttachmentPoint.MiddleCenter;
            mtext.TextHeight = settings.TextHeight;
            mtext.Layer = settings.LayerText;
            mtext.ColorIndex = settings.ColorText;
            btr.AppendEntity(mtext);
        }

        private void DrawMTextCell(BlockTableRecord btr, Point3d topLeft, double width, double height, string text, DrawingSettings settings)
        {
            DrawCellBorder(btr, topLeft, width, height, settings);
            if (string.IsNullOrEmpty(text)) return;

            var mtext = new MText();
            mtext.Contents = text;
            mtext.Width = width - (_config.CellPadding * 2);
            mtext.Location = new Point3d(topLeft.X + width / 2.0, topLeft.Y - height / 2.0, 0);
            mtext.Attachment = AttachmentPoint.MiddleCenter;
            mtext.TextHeight = settings.TextHeight;
            mtext.Layer = settings.LayerText;
            mtext.ColorIndex = settings.ColorText;
            btr.AppendEntity(mtext);
        }

        private void DrawBlockBorder(BlockTableRecord btr, Point3d origin, double totalHeight, DrawingSettings settings)
        {
            double totalWidth = _config.ColWidth_Mark + _config.ColWidth_Loc + _config.ColWidth_Section + (_config.ColWidth_Rebar * 2) + _config.ColWidth_Stirrup + _config.ColWidth_Web;

            var pl = new Polyline(4);
            pl.AddVertexAt(0, new Point2d(origin.X, origin.Y), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(origin.X + totalWidth, origin.Y), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(origin.X + totalWidth, origin.Y - totalHeight), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(origin.X, origin.Y - totalHeight), 0, 0, 0);
            pl.Closed = true;
            pl.Layer = settings.LayerDim;
            pl.ColorIndex = settings.ColorDim;
            pl.ConstantWidth = 2.0; // Nét đậm bao quanh Block dầm
            btr.AppendEntity(pl);
        }
    }
}
