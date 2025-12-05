using System;

namespace DTS_Wall_Tool.Core.Primitives
{
    /// <summary>
    /// Đoạn thẳng 2D có hướng từ Start đến End. 
    /// Sử dụng struct để tối ưu bộ nhớ. 
    /// </summary>
    public struct LineSegment2D
    {
        public Point2D Start;
        public Point2D End;

        #region Constructors

        public LineSegment2D(Point2D start, Point2D end)
        {
            Start = start;
            End = end;
        }

        public LineSegment2D(double x1, double y1, double x2, double y2)
        {
            Start = new Point2D(x1, y1);
            End = new Point2D(x2, y2);
        }

        #endregion

        #region Properties

        /// <summary>
        /// Chiều dài đoạn thẳng
        /// </summary>
        public double Length => Start.DistanceTo(End);

        /// <summary>
        /// Bình phương chiều dài (nhanh hơn khi chỉ cần so sánh)
        /// </summary>
        public double LengthSquared => Start.DistanceSquaredTo(End);

        /// <summary>
        /// Trung điểm đoạn thẳng
        /// </summary>
        public Point2D Midpoint => Start.MidpointTo(End);

        /// <summary>
        /// Vector hướng (chưa chuẩn hóa)
        /// </summary>
        public Point2D Delta => End - Start;

        /// <summary>
        /// Vector đơn vị chỉ hướng từ Start đến End
        /// </summary>
        public Point2D Direction => Delta.Normalized;

        /// <summary>
        /// Góc hướng của đoạn thẳng (radian, -PI đến PI)
        /// </summary>
        public double Angle => Math.Atan2(End.Y - Start.Y, End.X - Start.X);

        /// <summary>
        /// Góc chuẩn hóa [0, PI) - coi hai hướng ngược nhau là một
        /// </summary>
        public double NormalizedAngle
        {
            get
            {
                double a = Angle;
                while (a < 0) a += Math.PI;
                while (a >= Math.PI) a -= Math.PI;
                return a;
            }
        }

        /// <summary>
        /// Vector vuông góc đơn vị (quay 90 độ ngược chiều kim đồng hồ)
        /// </summary>
        public Point2D Normal => Direction.Perpendicular;

        /// <summary>
        /// Kiểm tra đoạn thẳng có độ dài hợp lệ không
        /// </summary>
        public bool IsValid => Length > GeometryConstants.EPSILON;

        #endregion

        #region Methods

        /// <summary>
        /// Lấy điểm tại vị trí t trên đoạn thẳng (t=0: Start, t=1: End)
        /// </summary>
        public Point2D PointAt(double t)
        {
            return new Point2D(
                Start.X + t * (End.X - Start.X),
                Start.Y + t * (End.Y - Start.Y)
            );
        }

        /// <summary>
        /// Đảo ngược hướng đoạn thẳng
        /// </summary>
        public LineSegment2D Reversed => new LineSegment2D(End, Start);

        /// <summary>
        /// Mở rộng đoạn thẳng theo cả hai hướng
        /// </summary>
        public LineSegment2D Extend(double amount)
        {
            Point2D dir = Direction;
            return new LineSegment2D(
                Start - dir * amount,
                End + dir * amount
            );
        }

        /// <summary>
        /// Dịch chuyển đoạn thẳng theo vector
        /// </summary>
        public LineSegment2D Translate(Point2D offset)
        {
            return new LineSegment2D(Start + offset, End + offset);
        }

        /// <summary>
        /// Tạo BoundingBox cho đoạn thẳng
        /// </summary>
        public BoundingBox GetBoundingBox() => new BoundingBox(this);

        #endregion

        #region Comparison

        public override bool Equals(object obj)
        {
            if (!(obj is LineSegment2D)) return false;
            var other = (LineSegment2D)obj;
            return Start.Equals(other.Start) && End.Equals(other.End);
        }

        /// <summary>
        /// So sánh bỏ qua hướng (A-B == B-A)
        /// </summary>
        public bool EqualsIgnoreDirection(LineSegment2D other)
        {
            return (Start.Equals(other.Start) && End.Equals(other.End)) ||
                   (Start.Equals(other.End) && End.Equals(other.Start));
        }

        public override int GetHashCode()
        {
            return Start.GetHashCode() ^ (End.GetHashCode() << 1);
        }

        #endregion

        #region String

        public override string ToString() => $"[{Start} -> {End}]";

        #endregion
    }
}