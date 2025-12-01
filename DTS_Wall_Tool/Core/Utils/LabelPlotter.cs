using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DTS_Wall_Tool.Core.Primitives;

namespace DTS_Wall_Tool.Core.Utils
{
    /// <summary>
    /// Vị trí chèn Label trên Frame
    /// </summary>
    public enum LabelPosition
    {
        /// <summary>Đầu frame, phía trên</summary>
        StartTop = 0,
        /// <summary>Đầu frame, phía dưới</summary>
        StartBottom = 1,
        /// <summary>Giữa frame, phía trên</summary>
        MiddleTop = 2,
        /// <summary>Giữa frame, phía dưới</summary>
        MiddleBottom = 3,
        /// <summary>Cuối frame, phía trên</summary>
        EndTop = 4,
        /// <summary>Cuối frame, phía dưới</summary>
        EndBottom = 5
    }

    /// <summary>
    /// Module vẽ MText Label thông minh cho Frame và Area
    /// Hỗ trợ 6 vị trí: Đầu/Giữa/Cuối × Trên/Dưới
    /// Text căn chỉnh tự động để không lẹm sang frame khác
    /// </summary>
    public static class LabelPlotter
    {
        #region Constants

        /// <summary>Khoảng cách từ text đến frame (mm)</summary>
        public const double TEXT_GAP = 50.0;

        /// <summary>Chiều cao text mặc định (mm)</summary>
        public const double DEFAULT_TEXT_HEIGHT = 80.0;

        /// <summary>Layer mặc định cho label</summary>
        public const string DEFAULT_LAYER = "dts_frame_label";

        /// <summary>Màu mặc định (254 = gray)</summary>
        public const int DEFAULT_COLOR = 254;

        #endregion

        #region Main API

        /// <summary>
        /// Vẽ MText Label trên Frame với vị trí và căn chỉnh thông minh
        /// </summary>
        /// <param name="btr">BlockTableRecord để thêm entity</param>
        /// <param name="tr">Transaction hiện tại</param>
        /// <param name="startPt">Điểm đầu của frame (CAD coordinates)</param>
        /// <param name="endPt">Điểm cuối của frame (CAD coordinates)</param>
        /// <param name="content">Nội dung MText (có thể chứa format codes như {\C1;text})</param>
        /// <param name="position">Vị trí chèn (6 vị trí)</param>
        /// <param name="textHeight">Chiều cao text (mm)</param>
        /// <param name="layer">Tên layer</param>
        /// <returns>ObjectId của MText đã tạo</returns>
        public static ObjectId PlotLabel(
            BlockTableRecord btr,
            Transaction tr,
            Point2D startPt,
            Point2D endPt,
            string content,
            LabelPosition position,
            double textHeight = DEFAULT_TEXT_HEIGHT,
            string layer = DEFAULT_LAYER)
        {
            if (btr == null || tr == null) return ObjectId.Null;
            if (string.IsNullOrWhiteSpace(content)) return ObjectId.Null;

            // Tính toán geometry
            var geo = CalculateLabelGeometry(startPt, endPt, position, textHeight);

            // Tạo MText
            MText mtext = new MText();
            mtext.Contents = content;
            mtext.Location = new Point3d(geo.InsertPoint.X, geo.InsertPoint.Y, 0);
            mtext.TextHeight = textHeight;
            mtext.Rotation = geo.Rotation;
            mtext.Attachment = geo.Attachment;
            mtext.Layer = layer;
            mtext.ColorIndex = DEFAULT_COLOR;

            // Thêm vào drawing
            ObjectId id = btr.AppendEntity(mtext);
            tr.AddNewlyCreatedDBObject(mtext, true);

            return id;
        }

        /// <summary>
        /// Vẽ MText Label với Point3d input
        /// </summary>
        public static ObjectId PlotLabel(
            BlockTableRecord btr,
            Transaction tr,
            Point3d startPt,
            Point3d endPt,
            string content,
            LabelPosition position,
            double textHeight = DEFAULT_TEXT_HEIGHT,
            string layer = DEFAULT_LAYER)
        {
            return PlotLabel(btr, tr,
                new Point2D(startPt.X, startPt.Y),
                new Point2D(endPt.X, endPt.Y),
                content, position, textHeight, layer);
        }

        /// <summary>
        /// Cập nhật nội dung MText hiện có
        /// </summary>
        public static void UpdateLabel(
            MText mtext,
            Point2D startPt,
            Point2D endPt,
            string content,
            LabelPosition position,
            double textHeight = DEFAULT_TEXT_HEIGHT)
        {
            if (mtext == null) return;

            var geo = CalculateLabelGeometry(startPt, endPt, position, textHeight);

            mtext.Contents = content;
            mtext.Location = new Point3d(geo.InsertPoint.X, geo.InsertPoint.Y, 0);
            mtext.TextHeight = textHeight;
            mtext.Rotation = geo.Rotation;
            mtext.Attachment = geo.Attachment;
        }

        #endregion

        #region Geometry Calculation

        /// <summary>
        /// Kết quả tính toán geometry cho label
        /// </summary>
        private struct LabelGeometry
        {
            public Point2D InsertPoint;
            public double Rotation;
            public AttachmentPoint Attachment;
        }

        /// <summary>
        /// Tính toán vị trí, góc xoay và attachment point cho label
        /// </summary>
        private static LabelGeometry CalculateLabelGeometry(
            Point2D startPt,
            Point2D endPt,
            LabelPosition position,
            double textHeight)
        {
            var result = new LabelGeometry();

            // 1. Tính vector hướng và góc
            double dx = endPt.X - startPt.X;
            double dy = endPt.Y - startPt.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);

            if (length < GeometryConstants.EPSILON)
            {
                result.InsertPoint = startPt;
                result.Rotation = 0;
                result.Attachment = AttachmentPoint.MiddleCenter;
                return result;
            }

            // Unit vector
            double ux = dx / length;
            double uy = dy / length;

            // Perpendicular vector (vuông góc, hướng "trên")
            double px = -uy;
            double py = ux;

            // 2. Tính góc và chuẩn hóa để text luôn đọc được (Bottom-Up, Left-Right)
            double angle = Math.Atan2(dy, dx);
            double readableAngle = NormalizeAngleForReadability(angle);

            // 3.  Xác định điểm base theo vị trí (Start/Middle/End)
            Point2D basePoint;
            switch (position)
            {
                case LabelPosition.StartTop:
                case LabelPosition.StartBottom:
                    basePoint = startPt;
                    break;
                case LabelPosition.EndTop:
                case LabelPosition.EndBottom:
                    basePoint = endPt;
                    break;
                default: // Middle
                    basePoint = new Point2D((startPt.X + endPt.X) / 2, (startPt.Y + endPt.Y) / 2);
                    break;
            }

            // 4. Xác định hướng offset (Trên/Dưới)
            double offsetDistance = textHeight * 0.8 + TEXT_GAP;
            bool isTopPosition = (position == LabelPosition.StartTop ||
                                  position == LabelPosition.MiddleTop ||
                                  position == LabelPosition.EndTop);

            // Nếu góc đã được xoay 180 độ, đảo hướng offset
            bool isFlipped = Math.Abs(readableAngle - angle) > 0.1;
            if (isFlipped) isTopPosition = !isTopPosition;

            double offsetMultiplier = isTopPosition ? 1.0 : -1.0;

            // 5.  Tính điểm chèn với offset
            result.InsertPoint = new Point2D(
                basePoint.X + px * offsetDistance * offsetMultiplier,
                basePoint.Y + py * offsetDistance * offsetMultiplier
            );

            result.Rotation = readableAngle;

            // 6.  Xác định Attachment Point
            // - Đầu frame: căn trái (Left)
            // - Giữa frame: căn giữa (Center)
            // - Cuối frame: căn phải (Right)
            // - Trên: căn đáy text (Bottom)
            // - Dưới: căn đỉnh text (Top)
            result.Attachment = GetAttachmentPoint(position, isFlipped);

            return result;
        }

        /// <summary>
        /// Chuẩn hóa góc để text luôn đọc được (không bị lộn ngược)
        /// </summary>
        private static double NormalizeAngleForReadability(double angle)
        {
            const double PI = Math.PI;

            // Chuyển về [0, 2π]
            double checkAngle = angle;
            if (checkAngle < 0) checkAngle += 2 * PI;

            // Nếu góc nằm trong nửa dưới (90° < angle <= 270°), xoay thêm 180°
            if (checkAngle > (PI / 2 + 0.001) && checkAngle <= (3 * PI / 2 + 0.001))
            {
                return angle + PI;
            }

            return angle;
        }

        /// <summary>
        /// Xác định AttachmentPoint dựa trên vị trí
        /// </summary>
        private static AttachmentPoint GetAttachmentPoint(LabelPosition position, bool isFlipped)
        {
            // Logic:
            // - Đầu (Start): căn LEFT để text mở rộng về phía giữa
            // - Giữa (Middle): căn CENTER
            // - Cuối (End): căn RIGHT để text mở rộng về phía giữa
            // - Trên: căn BOTTOM (chân text nằm gần frame)
            // - Dưới: căn TOP (đỉnh text nằm gần frame)

            bool isTop = (position == LabelPosition.StartTop ||
                          position == LabelPosition.MiddleTop ||
                          position == LabelPosition.EndTop);

            // Nếu đã flip, đảo logic top/bottom
            if (isFlipped) isTop = !isTop;

            switch (position)
            {
                case LabelPosition.StartTop:
                case LabelPosition.StartBottom:
                    return isTop ? AttachmentPoint.BottomLeft : AttachmentPoint.TopLeft;

                case LabelPosition.MiddleTop:
                case LabelPosition.MiddleBottom:
                    return isTop ? AttachmentPoint.BottomCenter : AttachmentPoint.TopCenter;

                case LabelPosition.EndTop:
                case LabelPosition.EndBottom:
                    return isTop ? AttachmentPoint.BottomRight : AttachmentPoint.TopRight;

                default:
                    return AttachmentPoint.MiddleCenter;
            }
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Tạo chuỗi MText với màu chỉ định
        /// Ví dụ: FormatWithColor("Hello", 1) => "{\C1;Hello}"
        /// </summary>
        public static string FormatWithColor(string text, int colorIndex)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return $"{{\\C{colorIndex};{text}}}";
        }

        /// <summary>
        /// Tạo chuỗi MText xuống dòng
        /// </summary>
        public static string CombineLines(params string[] lines)
        {
            return string.Join("\\P", lines);
        }

        /// <summary>
        /// Format nội dung mapping cho hiển thị
        /// </summary>
        public static string FormatMappingLabel(string frameName, string matchType, int colorIndex)
        {
            string coloredName = FormatWithColor(frameName, colorIndex);

            if (matchType == "FULL")
                return $"to {coloredName} (full)";
            else if (matchType == "NEW")
                return FormatWithColor("NEW", 1); // Đỏ
            else
                return $"to {coloredName}";
        }

        #endregion
    }
}