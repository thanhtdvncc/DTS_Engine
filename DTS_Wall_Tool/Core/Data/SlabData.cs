using System.Collections.Generic;

namespace DTS_Wall_Tool.Core.Data
{
    /// <summary>
    /// Dữ liệu Sàn - Kế thừa từ ElementData.
    /// </summary>
    public class SlabData : ElementData
    {
        #region Identity Override

        public override ElementType ElementType => ElementType.Slab;

        #endregion

        #region Slab-Specific Properties

        /// <summary>
        /// Chiều dày sàn (mm)
        /// </summary>
        public double? Thickness { get; set; } = null;

        /// <summary>
        /// Tên sàn (VD: "S120", "S150")
        /// </summary>
        public string SlabName { get; set; } = null;

        /// <summary>
        /// Loại sàn (Solid, Ribbed, Hollow...)
        /// </summary>
        public string SlabType { get; set; } = "Solid";

        /// <summary>
        /// Diện tích sàn (m2)
        /// </summary>
        public double? Area { get; set; } = null;

        /// <summary>
        /// Vật liệu
        /// </summary>
        public string Material { get; set; } = "Concrete";

        /// <summary>
        /// Tải trọng hoạt tải (kN/m2)
        /// </summary>
        public double? LiveLoad { get; set; } = null;

        /// <summary>
        /// Tải trọng hoàn thiện (kN/m2)
        /// </summary>
        public double? FinishLoad { get; set; } = null;

        #endregion

        #region Override Methods

        public override bool HasValidData()
        {
            return Thickness.HasValue || !string.IsNullOrEmpty(SlabName);
        }

        public override ElementData Clone()
        {
            var clone = new SlabData
            {
                Thickness = Thickness,
                SlabName = SlabName,
                SlabType = SlabType,
                Area = Area,
                Material = Material,
                LiveLoad = LiveLoad,
                FinishLoad = FinishLoad
            };

            CopyBaseTo(clone);
            return clone;
        }

        public override Dictionary<string, object> ToDictionary()
        {
            var dict = new Dictionary<string, object>();
            WriteBaseProperties(dict);

            if (Thickness.HasValue) dict["xThickness"] = Thickness.Value;
            if (!string.IsNullOrEmpty(SlabName)) dict["xSlabName"] = SlabName;
            if (!string.IsNullOrEmpty(SlabType)) dict["xSlabType"] = SlabType;
            if (Area.HasValue) dict["xArea"] = Area.Value;
            if (!string.IsNullOrEmpty(Material)) dict["xMaterial"] = Material;
            if (LiveLoad.HasValue) dict["xLiveLoad"] = LiveLoad.Value;
            if (FinishLoad.HasValue) dict["xFinishLoad"] = FinishLoad.Value;

            return dict;
        }

        public override void FromDictionary(Dictionary<string, object> dict)
        {
            ReadBaseProperties(dict);

            if (dict.TryGetValue("xThickness", out var t)) Thickness = ConvertToDouble(t);
            if (dict.TryGetValue("xSlabName", out var sn)) SlabName = sn?.ToString();
            if (dict.TryGetValue("xSlabType", out var st)) SlabType = st?.ToString();
            if (dict.TryGetValue("xArea", out var a)) Area = ConvertToDouble(a);
            if (dict.TryGetValue("xMaterial", out var mat)) Material = mat?.ToString();
            if (dict.TryGetValue("xLiveLoad", out var ll)) LiveLoad = ConvertToDouble(ll);
            if (dict.TryGetValue("xFinishLoad", out var fl)) FinishLoad = ConvertToDouble(fl);
        }

        #endregion

        #region Slab-Specific Methods

        public void EnsureSlabName()
        {
            if (string.IsNullOrEmpty(SlabName) && Thickness.HasValue)
            {
                SlabName = $"S{(int)Thickness.Value}";
            }
        }

        public override string ToString()
        {
            string thkStr = Thickness.HasValue ? $"{Thickness.Value:0}mm" : "[N/A]";
            string linkStatus = IsLinked ? "Linked" : "Unlinked";
            return $"Slab[{SlabName ?? "N/A"}] T={thkStr}, {SlabType}, {linkStatus}";
        }

        #endregion
    }
}