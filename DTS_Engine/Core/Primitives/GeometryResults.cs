namespace DTS_Wall_Tool.Core.Primitives
{
    /// <summary>
    /// Kết quả chiếu đoạn thẳng lên trục tham chiếu
    /// </summary>
    public struct GeometryResults
    {
        /// <summary>
        /// Tọa độ chiếu của điểm đầu trên trục
        /// </summary>
        public double StartProj;

        /// <summary>
        /// Tọa độ chiếu của điểm cuối trên trục
        /// </summary>
        public double EndProj;

        /// <summary>
        /// Giá trị chiếu nhỏ nhất
        /// </summary>
        public double MinProj => System.Math.Min(StartProj, EndProj);

        /// <summary>
        /// Giá trị chiếu lớn nhất
        /// </summary>
        public double MaxProj => System.Math.Max(StartProj, EndProj);

        /// <summary>
        /// Chiều dài trên trục chiếu
        /// </summary>
        public double Length => MaxProj - MinProj;

        public GeometryResults(double startProj, double endProj)
        {
            StartProj = startProj;
            EndProj = endProj;
        }

        public override string ToString() => $"Proj[{MinProj:0.0} - {MaxProj:0.0}]";
    }

    /// <summary>
    /// Kết quả tính toán chồng lấn giữa hai đoạn thẳng
    /// </summary>
    public struct OverlapResult
    {
        /// <summary>
        /// Có chồng lấn hay không
        /// </summary>
        public bool HasOverlap;

        /// <summary>
        /// Chiều dài phần chồng lấn
        /// </summary>
        public double OverlapLength;

        /// <summary>
        /// Tỷ lệ chồng lấn so với đoạn ngắn hơn (0-1)
        /// </summary>
        public double OverlapPercent;

        /// <summary>
        /// Điểm bắt đầu phần chồng lấn trên trục
        /// </summary>
        public double OverlapStart;

        /// <summary>
        /// Điểm kết thúc phần chồng lấn trên trục
        /// </summary>
        public double OverlapEnd;

        public override string ToString() =>
            HasOverlap ? $"Overlap[L={OverlapLength:0.0}, {OverlapPercent:P0}]" : "No Overlap";
    }

    /// <summary>
    /// Kết quả tìm giao điểm
    /// </summary>
    public struct IntersectionResult
    {
        /// <summary>
        /// Có giao điểm hay không
        /// </summary>
        public bool HasIntersection;

        /// <summary>
        /// Tọa độ giao điểm
        /// </summary>
        public Point2D Point;

        /// <summary>
        /// Tham số t trên đoạn thẳng 1 (0-1 nếu trong đoạn)
        /// </summary>
        public double T1;

        /// <summary>
        /// Tham số t trên đoạn thẳng 2 (0-1 nếu trong đoạn)
        /// </summary>
        public double T2;

        /// <summary>
        /// Giao điểm có nằm trong cả hai đoạn không
        /// </summary>
        public bool IsWithinBothSegments =>
            HasIntersection && T1 >= 0 && T1 <= 1 && T2 >= 0 && T2 <= 1;

        public override string ToString() =>
            HasIntersection ? $"Intersection[{Point}, t1={T1:0.00}, t2={T2:0.00}]" : "No Intersection";
    }

    /// <summary>
    /// Kết quả chiếu điểm lên đoạn thẳng
    /// </summary>
    public struct PointProjectionResult
    {
        /// <summary>
        /// Điểm chiếu trên đường thẳng
        /// </summary>
        public Point2D ProjectedPoint;

        /// <summary>
        /// Tham số t (0: Start, 1: End, ngoài [0,1] là ngoài đoạn)
        /// </summary>
        public double T;

        /// <summary>
        /// Khoảng cách từ điểm gốc đến điểm chiếu
        /// </summary>
        public double Distance;

        /// <summary>
        /// Điểm chiếu có nằm trong đoạn không
        /// </summary>
        public bool IsWithinSegment => T >= 0 && T <= 1;

        public override string ToString() => $"PointProj[{ProjectedPoint}, t={T:0. 00}, d={Distance:0. 0}]";
    }
}