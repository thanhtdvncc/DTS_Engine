namespace DTS_Engine.Core.Algorithms.Rebar.Models
{
    /// <summary>
    /// Ràng buộc BẮT BUỘC cho một dầm cụ thể.
    /// Truyền từ bên ngoài vào (VD: user lock, hoặc đồng bộ multi-beam).
    /// </summary>
    public class ExternalConstraints
    {
        /// <summary>
        /// Bắt buộc dùng đường kính này cho thép chủ.
        /// Null = tự do lựa chọn.
        /// </summary>
        public int? ForcedBackboneDiameter { get; set; }

        /// <summary>
        /// Bắt buộc số lượng thép chủ top.
        /// </summary>
        public int? ForcedBackboneCountTop { get; set; }

        /// <summary>
        /// Bắt buộc số lượng thép chủ bot.
        /// </summary>
        public int? ForcedBackboneCountBot { get; set; }

        /// <summary>
        /// Bắt buộc số nhánh đai.
        /// </summary>
        public int? ForcedStirrupLegs { get; set; }

        /// <summary>
        /// Nguồn gốc ràng buộc (để debug).
        /// VD: "UserLock", "MultiBeamSync", "StandardizedDiameter"
        /// </summary>
        public string Source { get; set; }
    }
}
