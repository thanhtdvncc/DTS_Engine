using System;

namespace DTS_Wall_Tool.Core.Primitives
{
    /// <summary>
    /// Hộp bao chữ nhật song song với trục (AABB - Axis-Aligned Bounding Box). 
    /// Dùng để kiểm tra nhanh trước khi tính toán chi tiết.
    /// </summary>
    public struct BoundingBox
    {
        public double MinX;
        public double MaxX;
        public double MinY;
        public double MaxY;

        #region Constructors

        public BoundingBox(double minX, double maxX, double minY, double maxY)
        {
            MinX = minX;
            MaxX = maxX;
            MinY = minY;
            MaxY = maxY;
        }

        public BoundingBox(Point2D p1, Point2D p2)
        {
            MinX = Math.Min(p1.X, p2.X);
            MaxX = Math.Max(p1.X, p2.X);
            MinY = Math.Min(p1.Y, p2.Y);
            MaxY = Math.Max(p1.Y, p2.Y);
        }

        public BoundingBox(LineSegment2D segment)
        {
            MinX = Math.Min(segment.Start.X, segment.End.X);
            MaxX = Math.Max(segment.Start.X, segment.End.X);
            MinY = Math.Min(segment.Start.Y, segment.End.Y);
            MaxY = Math.Max(segment.Start.Y, segment.End.Y);
        }

        #endregion

        #region Properties

        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
        public double Area => Width * Height;
        public Point2D Center => new Point2D((MinX + MaxX) / 2, (MinY + MaxY) / 2);
        public Point2D Min => new Point2D(MinX, MinY);
        public Point2D Max => new Point2D(MaxX, MaxY);

        /// <summary>
        /// Kiểm tra hộp bao có hợp lệ không (có diện tích dương)
        /// </summary>
        public bool IsValid => Width >= 0 && Height >= 0;

        #endregion

        #region Methods

        /// <summary>
        /// Kiểm tra giao với hộp bao khác
        /// </summary>
        public bool Intersects(BoundingBox other, double margin = 0)
        {
            return MaxX + margin >= other.MinX &&
                   MinX - margin <= other.MaxX &&
                   MaxY + margin >= other.MinY &&
                   MinY - margin <= other.MaxY;
        }

        /// <summary>
        /// Kiểm tra điểm nằm trong hộp bao
        /// </summary>
        public bool Contains(Point2D point, double margin = 0)
        {
            return point.X >= MinX - margin &&
                   point.X <= MaxX + margin &&
                   point.Y >= MinY - margin &&
                   point.Y <= MaxY + margin;
        }

        /// <summary>
        /// Kiểm tra hộp bao khác nằm hoàn toàn bên trong
        /// </summary>
        public bool Contains(BoundingBox other)
        {
            return other.MinX >= MinX &&
                   other.MaxX <= MaxX &&
                   other.MinY >= MinY &&
                   other.MaxY <= MaxY;
        }

        /// <summary>
        /// Mở rộng hộp bao theo tất cả các hướng
        /// </summary>
        public BoundingBox Expand(double margin)
        {
            return new BoundingBox(
                MinX - margin,
                MaxX + margin,
                MinY - margin,
                MaxY + margin
            );
        }

        /// <summary>
        /// Hợp nhất với hộp bao khác
        /// </summary>
        public BoundingBox Union(BoundingBox other)
        {
            return new BoundingBox(
                Math.Min(MinX, other.MinX),
                Math.Max(MaxX, other.MaxX),
                Math.Min(MinY, other.MinY),
                Math.Max(MaxY, other.MaxY)
            );
        }

        /// <summary>
        /// Giao với hộp bao khác
        /// </summary>
        public BoundingBox Intersection(BoundingBox other)
        {
            return new BoundingBox(
                Math.Max(MinX, other.MinX),
                Math.Min(MaxX, other.MaxX),
                Math.Max(MinY, other.MinY),
                Math.Min(MaxY, other.MaxY)
            );
        }

        /// <summary>
        /// Mở rộng để bao gồm điểm
        /// </summary>
        public BoundingBox Include(Point2D point)
        {
            return new BoundingBox(
                Math.Min(MinX, point.X),
                Math.Max(MaxX, point.X),
                Math.Min(MinY, point.Y),
                Math.Max(MaxY, point.Y)
            );
        }

        #endregion

        #region String

        public override string ToString() => $"BBox[({MinX:0.0},{MinY:0. 0})-({MaxX:0.0},{MaxY:0.0})]";

        #endregion

        #region Static Factory

        /// <summary>
        /// Tạo hộp bao từ danh sách điểm
        /// </summary>
        public static BoundingBox FromPoints(params Point2D[] points)
        {
            if (points == null || points.Length == 0)
                return new BoundingBox(0, 0, 0, 0);

            double minX = points[0].X, maxX = points[0].X;
            double minY = points[0].Y, maxY = points[0].Y;

            for (int i = 1; i < points.Length; i++)
            {
                if (points[i].X < minX) minX = points[i].X;
                if (points[i].X > maxX) maxX = points[i].X;
                if (points[i].Y < minY) minY = points[i].Y;
                if (points[i].Y > maxY) maxY = points[i].Y;
            }

            return new BoundingBox(minX, maxX, minY, maxY);
        }

        /// <summary>
        /// Hộp bao rỗng
        /// </summary>
        public static BoundingBox Empty => new BoundingBox(
            double.MaxValue, double.MinValue,
            double.MaxValue, double.MinValue
        );

        #endregion
    }
}