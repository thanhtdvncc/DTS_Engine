using System.Collections.Generic;

namespace DTS_Engine.Core.Algorithms.Rebar.Models
{
    /// <summary>
    /// Ràng buộc vĩ mô áp dụng cho toàn dự án/tầng/vùng.
    /// Dùng để đồng bộ thiết kế giữa các dầm.
    /// </summary>
    public class ProjectConstraints
    {
        // ═══════════════════════════════════════════════════════════════
        // STANDARDIZATION (Đồng bộ đường kính)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Đường kính ưu tiên toàn dự án (VD: D20).
        /// Null = không ép buộc.
        /// </summary>
        public int? PreferredMainDiameter { get; set; }

        /// <summary>
        /// Đường kính đai ưu tiên (VD: D10).
        /// </summary>
        public int? PreferredStirrupDiameter { get; set; }

        /// <summary>
        /// Danh sách đường kính BẮT BUỘC có trong kho
        /// (giới hạn inventory để giảm đa dạng thép).
        /// </summary>
        public List<int> AllowedDiametersOverride { get; set; }

        // ═══════════════════════════════════════════════════════════════
        // NEIGHBOR AWARENESS (Biết hàng xóm)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Thông tin các dầm đã thiết kế xong (để tham khảo).
        /// Key = GroupName, Value = Backbone config đã chọn.
        /// </summary>
        public Dictionary<string, NeighborDesign> NeighborDesigns { get; set; } = new Dictionary<string, NeighborDesign>();

        /// <summary>
        /// Trọng số ưu tiên khi backbone trùng với neighbor hoặc preferred diameter.
        /// </summary>
        public double NeighborMatchBonus { get; set; } = 5.0;
    }

    /// <summary>
    /// Thông tin thiết kế dầm lân cận (đã hoàn thành).
    /// </summary>
    public class NeighborDesign
    {
        public int BackboneDiameter { get; set; }
        public int BackboneCount { get; set; }
        public int StirrupDiameter { get; set; }
    }
}
