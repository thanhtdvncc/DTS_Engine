namespace DTS_Engine.Drawing.Models
{
    /// <summary>
    /// Định nghĩa cấu trúc lưới và kích thước cho bảng Section Drawing.
    /// Đơn vị: mm trên ModelSpace.
    /// </summary>
    public class TableLayoutConfig
    {
        // 1. Chiều cao hàng (Row Heights)
        public double RowHeight_Header { get; set; } = 300;     // Dòng tiêu đề chính
        public double RowHeight_Location { get; set; } = 250;   // Dòng END / CENTER
        public double RowHeight_Section { get; set; } = 2500;   // Khu vực vẽ mặt cắt (Cần rộng để đủ chỗ cho Dim)
        public double RowHeight_Text { get; set; } = 400;       // Chiều cao cho mỗi dòng text thép (TOP, BOT, STIR, WEB)

        // Tổng chiều cao một "Row con" (cho 1 mặt cắt) 
        // = Location + Section + 4 * Text (Top, Bot, Stirrup, Web)
        public double TotalRowHeight => RowHeight_Location + RowHeight_Section + (RowHeight_Text * 4);

        // 2. Chiều rộng cột (Column Widths)
        public double ColWidth_Mark { get; set; } = 1500;       // Cột gộp MARK(B x H)
        public double ColWidth_Loc { get; set; } = 800;         // Cột LOCATION nội bộ (nếu cần)
        public double ColWidth_Section { get; set; } = 3000;    // Cột chứa hình vẽ SECTION
        public double ColWidth_Rebar { get; set; } = 2000;      // Cột chứa Text TOP / BOTTOM
        public double ColWidth_Stirrup { get; set; } = 1500;    // Cột chứa Text STIRRUP
        public double ColWidth_Web { get; set; } = 1500;        // Cột chứa Text WEB BAR

        // 3. Cấu hình dàn trang (Pagination)
        public double BlockMarginY { get; set; } = 0;           // Khoảng cách giữa các Block dầm (thường = 0 để dính bảng)
        public double ColumnMarginX { get; set; } = 5000;       // Khoảng cách khi ngắt sang cột mới (5 mét)
        public int MaxBeamsPerColumn { get; set; } = 5;         // Tối đa 5 dầm trong 1 cột bảng trước khi ngắt

        // 4. Padding nội bộ ô
        public double CellPadding { get; set; } = 50;
    }
}
