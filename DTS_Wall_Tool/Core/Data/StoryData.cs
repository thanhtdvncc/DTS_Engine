namespace DTS_Wall_Tool.Core.Data
{
    /// <summary>
    /// Dữ liệu tầng (Story) lưu trong XData của origin marker
    /// </summary>
    public class StoryData
    {
        /// <summary>
        /// Tên tầng (VD: "Tang 1", "Tret")
        /// </summary>
        public string StoryName { get; set; }

        /// <summary>
        /// Cao độ tầng trong SAP2000 (mm)
        /// </summary>
        public double Elevation { get; set; }

        /// <summary>
        /// Chiều cao tầng (mm) - dùng để tính tải
        /// </summary>
        public double StoryHeight { get; set; } = 3300;

        /// <summary>
        /// Offset X từ gốc CAD đến gốc SAP2000
        /// </summary>
        public double OffsetX { get; set; } = 0;

        /// <summary>
        /// Offset Y từ gốc CAD đến gốc SAP2000
        /// </summary>
        public double OffsetY { get; set; } = 0;

        public override string ToString()
        {
            return $"Story[{StoryName}]: Z={Elevation:0}, H={StoryHeight:0}";
        }

        /// <summary>
        /// Clone StoryData
        /// </summary>
        public StoryData Clone()
        {
            return new StoryData
            {
                StoryName = StoryName,
                Elevation = Elevation,
                StoryHeight = StoryHeight,
                OffsetX = OffsetX,
                OffsetY = OffsetY
            };
        }
    }
}