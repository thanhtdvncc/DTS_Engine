using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DTS_Wall_Tool.Core.Primitives;
using System;

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
    /// Hỗ trợ6 vị trí: Đầu/Giữa/Cuối × Trên/Dưới
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
        /// Vẽ MText Label hỗ trợ3D đầy đủ (Vertical/Inclined)
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
            if (btr == null || tr == null || string.IsNullOrWhiteSpace(content)) return ObjectId.Null;

            // Tính toán Vector hướng3D
            Vector3d delta = endPt - startPt;
            double length3d = delta.Length;
            double length2d = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);

            Point3d insertPoint;
            double rotation = 0;
            AttachmentPoint attachment = AttachmentPoint.MiddleCenter;

            // CASE1: Phần tử thẳng đứng (Cột) - Vertical
            // Điều kiện: Độ dài2D gần bằng0 nhưng độ dài3D lớn
            if (length2d < GeometryConstants.EPSILON && length3d > GeometryConstants.EPSILON)
            {
                // Lấy trung điểm theo chiều cao Z
                Point3d midPt = new Point3d(
                    startPt.X,
                    startPt.Y,
                    (startPt.Z + endPt.Z) / 2.0
                );

                // Offset sang phải một chút (trục X) để không đè vào cột
                double offset = (textHeight / 2.0) + TEXT_GAP + 100.0; // +100 cho cột to
                insertPoint = new Point3d(midPt.X + offset, midPt.Y, midPt.Z);

                rotation = 0; // Text vẫn nằm ngang cho dễ đọc
                attachment = AttachmentPoint.MiddleLeft;
            }
            // CASE2: Phần tử xiên hoặc nằm ngang (Dầm/Giằng)
            else
            {
                // Tính toán vị trí trên mặt phẳng2D (XY) nhưng giữ cao độ Z
                Point2D s2 = new Point2D(startPt.X, startPt.Y);
                Point2D e2 = new Point2D(endPt.X, endPt.Y);

                var geo = CalculateLabelGeometry(s2, e2, position, textHeight);

                // Nội suy cao độ Z tại điểm chèn
                // Nếu là Middle thì lấy Average Z
                // Nếu là Start/End thì lấy Z tương ứng
                double z = 0;
                if (position == LabelPosition.StartTop || position == LabelPosition.StartBottom)
                    z = startPt.Z;
                else if (position == LabelPosition.EndTop || position == LabelPosition.EndBottom)
                    z = endPt.Z;
                else
                    z = (startPt.Z + endPt.Z) / 2.0;

                insertPoint = new Point3d(geo.InsertPoint.X, geo.InsertPoint.Y, z);
                rotation = geo.Rotation;
                attachment = geo.Attachment;
            }

            // Tạo MText
            MText mtext = new MText();
            mtext.Contents = content;
            mtext.Location = insertPoint;
            mtext.TextHeight = textHeight;
            mtext.Rotation = rotation;
            mtext.Attachment = attachment;
            mtext.Layer = layer;
            mtext.ColorIndex = DEFAULT_COLOR;

            ObjectId id = btr.AppendEntity(mtext);
            tr.AddNewlyCreatedDBObject(mtext, true);

            return id;
        }

        public static ObjectId PlotPointLabel(
            BlockTableRecord btr,
            Transaction tr,
            Point2D center,
            string content,
            double textHeight = DEFAULT_TEXT_HEIGHT,
            string layer = DEFAULT_LAYER)
        {
            // Giữ nguyên logic Point cũ cho2D
            if (btr == null || tr == null) return ObjectId.Null;

            MText mtext = new MText();
            mtext.Contents = content;
            mtext.Location = new Point3d(center.X, center.Y + TEXT_GAP, 0);
            mtext.TextHeight = textHeight;
            mtext.Attachment = AttachmentPoint.BottomCenter;
            mtext.Layer = layer;
            mtext.ColorIndex = DEFAULT_COLOR;

            ObjectId id = btr.AppendEntity(mtext);
            tr.AddNewlyCreatedDBObject(mtext, true);
            return id;
        }

        private struct LabelGeometry
        {
            public Point2D InsertPoint;
            public double Rotation;
            public AttachmentPoint Attachment;
        }

        private static LabelGeometry CalculateLabelGeometry(
            Point2D startPt, Point2D endPt,
            LabelPosition position, double textHeight)
        {
            var result = new LabelGeometry();
            double dx = endPt.X - startPt.X;
            double dy = endPt.Y - startPt.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);

            if (length < GeometryConstants.EPSILON)
            {
                result.InsertPoint = new Point2D(startPt.X + TEXT_GAP, startPt.Y);
                result.Rotation = 0;
                result.Attachment = AttachmentPoint.MiddleLeft;
                return result;
            }

            double ux = dx / length;
            double uy = dy / length;
            double angle = Math.Atan2(dy, dx);
            if (angle < 0) angle += 2 * Math.PI;

            bool isFlipped = (angle > Math.PI / 2 && angle <= 3 * Math.PI / 2);
            double readableAngle = isFlipped ? angle + Math.PI : angle;

            double perpX = -uy;
            double perpY = ux;

            Point2D basePoint;
            switch (position)
            {
                case LabelPosition.StartTop:
                case LabelPosition.StartBottom: basePoint = startPt; break;
                case LabelPosition.EndTop:
                case LabelPosition.EndBottom: basePoint = endPt; break;
                default: basePoint = new Point2D((startPt.X + endPt.X) / 2, (startPt.Y + endPt.Y) / 2); break;
            }

            bool isTopPos = (position == LabelPosition.StartTop || position == LabelPosition.MiddleTop || position == LabelPosition.EndTop);
            double offsetDist = (textHeight / 2.0) + TEXT_GAP;
            double directionMultiplier = isTopPos ? 1.0 : -1.0;
            if (isFlipped) directionMultiplier *= -1.0;

            result.InsertPoint = new Point2D(
                basePoint.X + perpX * offsetDist * directionMultiplier,
                basePoint.Y + perpY * offsetDist * directionMultiplier
            );

            result.Rotation = readableAngle;

            if (isTopPos) result.Attachment = AttachmentPoint.BottomCenter;
            else result.Attachment = AttachmentPoint.TopCenter;

            return result;
        }
        #endregion

        #region Utility Methods

        /// <summary>
        /// Tạo chuỗi MText với màu chỉ định
        /// Ví dụ: FormatWithColor("Hello", 1) => "{\C1;text}"
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