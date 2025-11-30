using DTS_Wall_Tool.Core.Primitives;

namespace DTS_Wall_Tool.Core.Interfaces
{
    /// <summary>
    /// Interface cho các đối tượng có hình học đoạn thẳng. 
    /// Loại bỏ trùng lặp giữa WallSegment, CenterLine, AxisLine. 
    /// </summary>
    public interface ILineGeometry
    {
        /// <summary>
        /// Điểm đầu
        /// </summary>
        Point2D StartPt { get; set; }

        /// <summary>
        /// Điểm cuối
        /// </summary>
        Point2D EndPt { get; set; }

        /// <summary>
        /// Chiều dài đoạn thẳng
        /// </summary>
        double Length { get; }

        /// <summary>
        /// Trung điểm
        /// </summary>
        Point2D Midpoint { get; }

        /// <summary>
        /// Góc hướng (radian)
        /// </summary>
        double Angle { get; }

        /// <summary>
        /// Chuyển đổi sang LineSegment2D
        /// </summary>
        LineSegment2D AsSegment { get; }
    }
}