using System;

namespace DTS_Engine.Core.Data
{
    /// <summary>
    /// Cấu hình bản vẽ mặt cắt dầm (Layers, Colors, Geometry).
    /// </summary>
    [Serializable]
    public class DrawingSettings
    {
        // 1. Concrete Settings
        public string LayerConcrete { get; set; } = "S-BEAM-CONC";
        public int ColorConcrete { get; set; } = 2; // Yellow

        // 2. Main Rebar Settings
        public string LayerMainRebar { get; set; } = "S-BEAM-MAIN";
        public int ColorMainRebar { get; set; } = 3; // Green

        // 3. Stirrup Settings
        public string LayerStirrup { get; set; } = "S-BEAM-STIRRUP";
        public int ColorStirrup { get; set; } = 1; // Red
        public bool DrawStirrupHook { get; set; } = true;

        public string LayerSideBar { get; set; } = "S_SIDEBAR";
        public int ColorSideBar { get; set; } = 3;

        // 4. Dimension & Text Settings
        public string LayerDim { get; set; } = "S-BEAM-DIM";
        public int ColorDim { get; set; } = 8; // Gray
        public string LayerText { get; set; } = "S-BEAM-TEXT";
        public int ColorText { get; set; } = 7; // White

        // 5. Geometry Params (mm)
        public double ConcreteCover { get; set; } = 25.0;
        public double TextHeight { get; set; } = 250.0; // Chiều cao chữ (mm trên ModelSpace)
        public double DimScale { get; set; } = 1.0;
        public double MaxTableHeight { get; set; } = 15000.0; // Chiều cao tối đa 1 cột bảng (mm)
    }
}
