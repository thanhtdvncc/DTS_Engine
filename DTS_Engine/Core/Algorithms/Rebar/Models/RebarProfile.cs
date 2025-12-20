using System.Collections.Generic;
using System.Linq;

namespace DTS_Engine.Core.Algorithms.Rebar.Models
{
    /// <summary>
    /// Lưu vị trí tọa độ (X, Y) của các thanh thép trong mặt cắt.
    /// Dùng để StirrupCalculator biết cần bao nhiêu nhánh đai để ôm hết thép dọc.
    /// </summary>
    public class RebarProfile
    {
        /// <summary>Thép trên lớp 1</summary>
        public List<BarPosition> TopLayer1 { get; set; } = new List<BarPosition>();

        /// <summary>Thép trên lớp 2</summary>
        public List<BarPosition> TopLayer2 { get; set; } = new List<BarPosition>();

        /// <summary>Thép dưới lớp 1</summary>
        public List<BarPosition> BotLayer1 { get; set; } = new List<BarPosition>();

        /// <summary>Thép dưới lớp 2</summary>
        public List<BarPosition> BotLayer2 { get; set; } = new List<BarPosition>();

        /// <summary>Thép cạnh (chống xoắn)</summary>
        public List<BarPosition> SideBars { get; set; } = new List<BarPosition>();

        /// <summary>
        /// Lấy tất cả thanh thép cần được đai ôm.
        /// </summary>
        public IEnumerable<BarPosition> GetAllBarsRequiringTies()
        {
            return TopLayer1.Concat(TopLayer2)
                .Concat(BotLayer1).Concat(BotLayer2)
                .Where(b => b.RequiresTie);
        }

        /// <summary>
        /// Đếm số thanh ở hàng ngoài cùng (cần đai ôm).
        /// </summary>
        public int GetOuterBarCount()
        {
            int topOuter = TopLayer1.Count(b => b.IsCorner || b.RequiresTie);
            int botOuter = BotLayer1.Count(b => b.IsCorner || b.RequiresTie);
            return topOuter + botOuter + SideBars.Count;
        }
    }

    /// <summary>
    /// Vị trí một thanh thép trong mặt cắt.
    /// </summary>
    public class BarPosition
    {
        /// <summary>Tọa độ X từ mép trái (mm)</summary>
        public double X { get; set; }

        /// <summary>Tọa độ Y từ đáy (mm)</summary>
        public double Y { get; set; }

        /// <summary>Đường kính thanh (mm)</summary>
        public int Diameter { get; set; }

        /// <summary>Lớp (1, 2, 3)</summary>
        public int Layer { get; set; }

        /// <summary>Có cần đai ôm không? (thường = true trừ thanh giữa layer 2+)</summary>
        public bool RequiresTie { get; set; } = true;

        /// <summary>Có phải thanh góc không?</summary>
        public bool IsCorner { get; set; } = false;
    }
}
