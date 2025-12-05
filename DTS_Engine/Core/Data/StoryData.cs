using System.Collections.Generic;

namespace DTS_Engine.Core.Data
{
    /// <summary>
    /// Dữ liệu Tầng (Story) - Origin marker. 
    /// Đây là đối tượng đặc biệt, không kế thừa ElementData vì nó là CHA của các phần tử. 
    /// </summary>
    public class StoryData
    {
        #region Identity

        /// <summary>
        /// Luôn trả về StoryOrigin
        /// </summary>
        public ElementType ElementType => ElementType.StoryOrigin;

        /// <summary>
        /// Mã xType trong XData
        /// </summary>
        public string XType => "STORY_ORIGIN";

        #endregion

        #region Story Properties

        /// <summary>
        /// Tên tầng (VD: "Tang 1", "Tret", "Lung")
        /// </summary>
        public string StoryName { get; set; }

        /// <summary>
        /// Cao độ đáy tầng trong SAP2000 (mm)
        /// </summary>
        public double Elevation { get; set; }

        /// <summary>
        /// Chiều cao tầng (mm) - dùng để tính tải tường
        /// </summary>
        public double StoryHeight { get; set; } = 3300;

        #endregion

        #region Coordinate Offset

        /// <summary>
        /// Offset X từ gốc CAD đến gốc SAP2000 (mm)
        /// </summary>
        public double OffsetX { get; set; } = 0;

        /// <summary>
        /// Offset Y từ gốc CAD đến gốc SAP2000 (mm)
        /// </summary>
        public double OffsetY { get; set; } = 0;

        #endregion

        #region Linked Children

        /// <summary>
        /// Danh sách Handle của các phần tử con đã liên kết
        /// </summary>
        public List<string> ChildHandles { get; set; } = new List<string>();

        /// <summary>
        /// Số lượng phần tử con
        /// </summary>
        public int ChildCount => ChildHandles?.Count ?? 0;

        #endregion

        #region Methods

        public Dictionary<string, object> ToDictionary()
        {
            var dict = new Dictionary<string, object>
            {
                ["xType"] = XType,
                ["xStoryName"] = StoryName,
                ["xElevation"] = Elevation,
                ["xStoryHeight"] = StoryHeight,
                ["xOffsetX"] = OffsetX,
                ["xOffsetY"] = OffsetY
            };

            if (ChildHandles != null && ChildHandles.Count > 0)
                dict["xChildHandles"] = ChildHandles;

            return dict;
        }

        public void FromDictionary(Dictionary<string, object> dict)
        {
            if (dict.TryGetValue("xStoryName", out var name))
                StoryName = name?.ToString();

            if (dict.TryGetValue("xElevation", out var elev) && double.TryParse(elev?.ToString(), out double e))
                Elevation = e;

            if (dict.TryGetValue("xStoryHeight", out var height) && double.TryParse(height?.ToString(), out double h))
                StoryHeight = h;

            if (dict.TryGetValue("xOffsetX", out var ox) && double.TryParse(ox?.ToString(), out double x))
                OffsetX = x;

            if (dict.TryGetValue("xOffsetY", out var oy) && double.TryParse(oy?.ToString(), out double y))
                OffsetY = y;

            if (dict.TryGetValue("xChildHandles", out var children))
            {
                ChildHandles = new List<string>();
                if (children is System.Collections.IEnumerable enumerable && !(children is string))
                {
                    foreach (var item in enumerable)
                    {
                        if (item != null) ChildHandles.Add(item.ToString());
                    }
                }
            }
        }

        public StoryData Clone()
        {
            var clone = new StoryData
            {
                StoryName = StoryName,
                Elevation = Elevation,
                StoryHeight = StoryHeight,
                OffsetX = OffsetX,
                OffsetY = OffsetY,
                ChildHandles = new List<string>(ChildHandles ?? new List<string>())
            };
            return clone;
        }

        public override string ToString()
        {
            return $"Story[{StoryName}]: Z={Elevation:0}mm, H={StoryHeight:0}mm, Children={ChildCount}";
        }

        #endregion
    }
}