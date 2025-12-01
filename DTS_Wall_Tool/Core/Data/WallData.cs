using System.Collections.Generic;

namespace DTS_Wall_Tool.Core.Data
{
    /// <summary>
    /// Dữ liệu Tường - Kế thừa từ ElementData. 
    /// Chỉ chứa các thuộc tính đặc thù của Tường.
    /// </summary>
    public class WallData : ElementData
    {
        #region Identity Override

        public override ElementType ElementType => ElementType.Wall;

        #endregion

        #region Wall-Specific Properties

        /// <summary>
        /// Độ dày tường (mm)
        /// </summary>
        public double? Thickness { get; set; } = null;

        /// <summary>
        /// Loại tường (VD: "W220", "W200")
        /// </summary>
        public string WallType { get; set; } = null;

        /// <summary>
        /// Loại vật liệu tường (Brick, Concrete, Block...)
        /// </summary>
        public string Material { get; set; } = "Brick";

        /// <summary>
        /// Trọng lượng riêng vật liệu (kN/m3)
        /// </summary>
        public double? UnitWeight { get; set; } = 18.0;

        #endregion

        #region Load Properties

        /// <summary>
        /// Load pattern trong SAP2000
        /// </summary>
        public string LoadPattern { get; set; } = "DL";

        /// <summary>
        /// Giá trị tải tính toán (kN/m)
        /// </summary>
        public double? LoadValue { get; set; } = null;

        /// <summary>
        /// Hệ số điều chỉnh tải (mặc định 1. 0)
        /// </summary>
        public double LoadFactor { get; set; } = 1.0;

        #endregion

        #region Override Methods

        public override bool HasValidData()
        {
            return Thickness.HasValue || !string.IsNullOrEmpty(WallType) || LoadValue.HasValue;
        }

        public override ElementData Clone()
        {
            var clone = new WallData
            {
                Thickness = Thickness,
                WallType = WallType,
                Material = Material,
                UnitWeight = UnitWeight,
                LoadPattern = LoadPattern,
                LoadValue = LoadValue,
                LoadFactor = LoadFactor
            };

            // Copy base properties
            CopyBaseTo(clone);

            return clone;
        }

        public override Dictionary<string, object> ToDictionary()
        {
            var dict = new Dictionary<string, object>();

            // Write base properties
            WriteBaseProperties(dict);

            // Write wall-specific properties
            if (Thickness.HasValue)
                dict["xThickness"] = Thickness.Value;

            if (!string.IsNullOrEmpty(WallType))
                dict["xWallType"] = WallType;

            if (!string.IsNullOrEmpty(Material))
                dict["xMaterial"] = Material;

            if (UnitWeight.HasValue)
                dict["xUnitWeight"] = UnitWeight.Value;

            if (!string.IsNullOrEmpty(LoadPattern))
                dict["xLoadPattern"] = LoadPattern;

            if (LoadValue.HasValue)
                dict["xLoadValue"] = LoadValue.Value;

            dict["xLoadFactor"] = LoadFactor;

            return dict;
        }

        public override void FromDictionary(Dictionary<string, object> dict)
        {
            // Read base properties
            ReadBaseProperties(dict);

            // Read wall-specific properties
            if (dict.TryGetValue("xThickness", out var thickness))
                Thickness = ConvertToDouble(thickness);

            if (dict.TryGetValue("xWallType", out var wallType))
                WallType = wallType?.ToString();

            if (dict.TryGetValue("xMaterial", out var material))
                Material = material?.ToString();

            if (dict.TryGetValue("xUnitWeight", out var unitWeight))
                UnitWeight = ConvertToDouble(unitWeight);

            if (dict.TryGetValue("xLoadPattern", out var loadPattern))
                LoadPattern = loadPattern?.ToString();

            if (dict.TryGetValue("xLoadValue", out var loadValue))
                LoadValue = ConvertToDouble(loadValue);

            if (dict.TryGetValue("xLoadFactor", out var loadFactor))
                LoadFactor = ConvertToDouble(loadFactor) ?? 1.0;
        }

        #endregion

        #region Wall-Specific Methods

        /// <summary>
        /// Tự động tạo WallType từ Thickness
        /// </summary>
        public void EnsureWallType()
        {
            if (string.IsNullOrEmpty(WallType) && Thickness.HasValue && Thickness.Value > 0)
            {
                WallType = "W" + ((int)Thickness.Value).ToString();
            }
        }

        /// <summary>
        /// Tính tải trọng tường (kN/m)
        /// LoadValue = Thickness(m) * Height(m) * UnitWeight(kN/m3) * LoadFactor
        /// </summary>
        public void CalculateLoad()
        {
            if (!Thickness.HasValue || !Height.HasValue || !UnitWeight.HasValue) return;

            double thicknessM = Thickness.Value / 1000.0;
            double heightM = Height.Value / 1000.0;

            LoadValue = thicknessM * heightM * UnitWeight.Value * LoadFactor;
        }

        public override string ToString()
        {
            string thkStr = Thickness.HasValue ? $"{Thickness.Value:0}mm" : "[N/A]";
            string loadStr = LoadValue.HasValue ? $"{LoadValue.Value:0. 00}kN/m" : "[N/A]";
            string linkStatus = IsLinked ? $"Linked:{OriginHandle}" : "Unlinked";

            return $"Wall[{WallType ?? "N/A"}] T={thkStr}, Load={loadStr}, {linkStatus}";
        }

        #endregion
    }
}