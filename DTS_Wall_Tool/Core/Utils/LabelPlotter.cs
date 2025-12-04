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
            Vector3d axisDir = delta.GetNormal();
            Vector3d up = new Vector3d(0, 0, 1);

            // Nếu gần thẳng đứng
            bool isVertical = length2d < GeometryConstants.EPSILON && length3d > GeometryConstants.EPSILON;

            // SELECT base point
            Point3d basePt;
            if (position == LabelPosition.StartTop || position == LabelPosition.StartBottom)
                basePt = startPt;
            else if (position == LabelPosition.EndTop || position == LabelPosition.EndBottom)
                basePt = endPt;
            else
                basePt = new Point3d((startPt.X + endPt.X) / 2.0, (startPt.Y + endPt.Y) / 2.0, (startPt.Z + endPt.Z) / 2.0);

            // Compute textNormal and textDirection (baseline)
            Vector3d textNormal;
            if (isVertical)
            {
                // For columns prefer a horizontal normal (push text away in X)
                textNormal = new Vector3d(1, 0, 0);
                axisDir = up; // baseline along Z for column text (text runs up)
            }
            else
            {
                // For beams/slanted: normal is cross(axis, globalZ) -> horizontal vector
                textNormal = axisDir.CrossProduct(up);
                if (textNormal.Length < 0.001) textNormal = new Vector3d(0, 0, 1);
                else textNormal = textNormal.GetNormal();
            }

            // Up direction for text is cross(normal, axis)
            Vector3d textUp = textNormal.CrossProduct(axisDir).GetNormal();

            // Offset distance
            bool isTop = (position == LabelPosition.StartTop || position == LabelPosition.MiddleTop || position == LabelPosition.EndTop);
            double offset = (textHeight / 2.0) + TEXT_GAP;

            if (isVertical)
            {
                offset += 100.0; // extra gap for columns
                insertPoint = basePt + textNormal * offset;
            }
            else
            {
                double sign = isTop ? 1.0 : -1.0;
                insertPoint = basePt + textUp * (offset * sign);
            }

            // Create MText with3D orientation
            MText mtext = new MText();
            mtext.Contents = content;
            mtext.Location = insertPoint;
            mtext.TextHeight = textHeight;
            mtext.Layer = layer;
            mtext.ColorIndex = DEFAULT_COLOR;

            // IMPORTANT: set Normal and Direction for proper3D orientation
            mtext.Normal = textNormal;
            mtext.Direction = axisDir.GetNormal();

            // Attachment selection
            if (isVertical)
            {
                mtext.Attachment = AttachmentPoint.MiddleLeft;
            }
            else
            {
                if (position == LabelPosition.MiddleTop || position == LabelPosition.MiddleBottom)
                    mtext.Attachment = isTop ? AttachmentPoint.BottomCenter : AttachmentPoint.TopCenter;
                else if (position.ToString().StartsWith("Start"))
                    mtext.Attachment = isTop ? AttachmentPoint.BottomLeft : AttachmentPoint.TopLeft;
                else
                    mtext.Attachment = isTop ? AttachmentPoint.BottomRight : AttachmentPoint.TopRight;
            }

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