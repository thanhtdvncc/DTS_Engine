namespace DTS_Engine.Core.Data
{
    /// <summary>
    /// Định danh loại phần tử xây dựng. 
    /// Sử dụng trong Factory Pattern để tự động tạo đúng loại ElementData.
    /// </summary>
    public enum ElementType
    {
        /// <summary>Không xác định</summary>
        Unknown = 0,

        // ========== Structural Elements ==========
        /// <summary>Dầm (Line/Polyline)</summary>
        Beam = 1,

        /// <summary>Cột (Polyline/Block)</summary>
        Column = 2,

        /// <summary>Sàn (Polyline)</summary>
        Slab = 3,

        /// <summary>Tường (Line)</summary>
        Wall = 4,

        /// <summary>Móng (Polyline)</summary>
        Foundation = 5,

        /// <summary>Cầu thang (Polyline)</summary>
        Stair = 6,

        /// <summary>Cọc (Line)</summary>
        Pile = 7,

        /// <summary>Lanh tô (Line)</summary>
        Lintel = 8,

        /// <summary>Cốt thép (Line/Polyline)</summary>
        Rebar = 9,

        /// <summary>Vách (Line/Polyline)</summary>
        ShearWall = 10,

        // ========== Origin Markers ==========
        /// <summary>Điểm gốc tầng</summary>
        StoryOrigin = 99,

        /// <summary>Điểm gốc phần tử</summary>
        ElementOrigin = 100
    }

    /// <summary>
    /// Extension methods cho ElementType
    /// </summary>
    public static class ElementTypeExtensions
    {
        /// <summary>
        /// Chuyển string sang ElementType
        /// </summary>
        public static ElementType ParseElementType(string xType)
        {
            if (string.IsNullOrEmpty(xType)) return ElementType.Unknown;

            switch (xType.ToUpperInvariant())
            {
                case "WALL": return ElementType.Wall;
                case "COLUMN": return ElementType.Column;
                case "BEAM": return ElementType.Beam;
                case "SLAB": return ElementType.Slab;
                case "FOUNDATION": return ElementType.Foundation;
                case "STAIR": return ElementType.Stair;
                case "PILE": return ElementType.Pile;
                case "LINTEL": return ElementType.Lintel;
                case "REBAR": return ElementType.Rebar;
                case "SHEARWALL": return ElementType.ShearWall;
                case "STORY_ORIGIN": return ElementType.StoryOrigin;
                case "ELEMENT_ORIGIN": return ElementType.ElementOrigin;
                default: return ElementType.Unknown;
            }
        }

        /// <summary>
        /// Kiểm tra có phải phần tử kết cấu không
        /// </summary>
        public static bool IsStructuralElement(this ElementType type)
        {
            return type >= ElementType.Beam && type <= ElementType.ShearWall;
        }

        /// <summary>
        /// Kiểm tra có phải origin marker không
        /// </summary>
        public static bool IsOriginMarker(this ElementType type)
        {
            return type == ElementType.StoryOrigin || type == ElementType.ElementOrigin;
        }
    }
}