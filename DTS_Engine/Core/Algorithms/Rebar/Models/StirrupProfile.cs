using System.Collections.Generic;

namespace DTS_Engine.Core.Algorithms.Rebar.Models
{
    /// <summary>
    /// Profile đai dọc theo chiều dài dầm.
    /// </summary>
    public class StirrupProfile
    {
        /// <summary>Các zone đai khác nhau (near support, mid-span, etc.)</summary>
        public List<StirrupZone> Zones { get; set; } = new List<StirrupZone>();

        /// <summary>Đường kính đai chính</summary>
        public int MainDiameter { get; set; }

        /// <summary>Số nhánh đai tại mặt cắt điển hình</summary>
        public int TypicalLegCount { get; set; }
    }

    /// <summary>
    /// Một zone đai (VD: vùng gần gối có bước dày hơn).
    /// </summary>
    public class StirrupZone
    {
        /// <summary>Vị trí bắt đầu (m từ đầu dầm)</summary>
        public double StartPosition { get; set; }

        /// <summary>Vị trí kết thúc (m)</summary>
        public double EndPosition { get; set; }

        /// <summary>Bước đai (mm)</summary>
        public int Spacing { get; set; }

        /// <summary>Đường kính đai</summary>
        public int Diameter { get; set; }

        /// <summary>Số nhánh</summary>
        public int LegCount { get; set; }

        /// <summary>Loại zone: "Support", "MidSpan", "Transition"</summary>
        public string ZoneType { get; set; }
    }
}
