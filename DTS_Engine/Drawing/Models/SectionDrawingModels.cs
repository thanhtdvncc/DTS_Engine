using System.Collections.Generic;

namespace DTS_Engine.Drawing.Models
{
    public class RebarLayer
    {
        public int Count { get; set; }
        public int Diameter { get; set; }
        public double Area { get; set; }
    }

    public class StirrupInfo
    {
        public int Diameter { get; set; }
        public int Spacing { get; set; }
        public int Legs { get; set; }
    }

    public class SectionCellData
    {
        public string LocationName { get; set; } // "END" | "CENTER"
        public int DataIndex { get; set; }
        public double Width { get; set; }  // mm
        public double Height { get; set; } // mm
        public double Cover { get; set; }  // mm
        public List<RebarLayer> TopLayers { get; set; } = new List<RebarLayer>();
        public List<RebarLayer> BotLayers { get; set; } = new List<RebarLayer>();
        public StirrupInfo Stirrup { get; set; }
        public string TopText { get; set; }  // Raw string for table
        public string BotText { get; set; }
        public string StirrupText { get; set; }
        public string WebText { get; set; }
    }

    public class BeamScheduleRowData
    {
        public string BeamName { get; set; }
        public string SizeLabel { get; set; } // "220x400"
        public List<SectionCellData> Cells { get; set; } = new List<SectionCellData>();
    }
}
