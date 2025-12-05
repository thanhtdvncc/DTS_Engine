using System.Collections.Generic;

namespace DTS_Wall_Tool.Core.Data
{
    /// <summary>
    /// D? li?u C?t thép - K? th?a t? ElementData.
 /// </summary>
    public class RebarData : ElementData
    {
        #region Identity Override

        public override ElementType ElementType => ElementType.Rebar;

        #endregion

        #region Rebar-Specific Properties

        /// <summary>
        /// ???ng kính c?t thép (mm)
        /// </summary>
        public double? Diameter { get; set; } = null;

        /// <summary>
     /// Chi?u dài c?t thép (mm)
      /// </summary>
    public double? Length { get; set; } = null;

        /// <summary>
/// Ký hi?u c?t thép (VD: "D10", "D12", "D16")
/// </summary>
  public string RebarMark { get; set; } = null;

        /// <summary>
/// Lo?i c?t thép (Main, Stirrup, Distribution...)
        /// </summary>
      public string RebarType { get; set; } = "Main";

        /// <summary>
        /// C?p thép (CB240, CB300, CB400...)
        /// </summary>
   public string SteelGrade { get; set; } = "CB300";

        /// <summary>
  /// S? l??ng thanh
        /// </summary>
      public int? Quantity { get; set; } = null;

        /// <summary>
        /// Kho?ng cách b? trí (mm) - cho c?t ?ai
        /// </summary>
        public double? Spacing { get; set; } = null;

        #endregion

     #region Override Methods

    public override bool HasValidData()
        {
        return Diameter.HasValue || !string.IsNullOrEmpty(RebarMark);
  }

        public override ElementData Clone()
        {
  var clone = new RebarData
            {
      Diameter = Diameter,
      Length = Length,
     RebarMark = RebarMark,
       RebarType = RebarType,
           SteelGrade = SteelGrade,
 Quantity = Quantity,
        Spacing = Spacing
};

            CopyBaseTo(clone);
      return clone;
        }

    public override Dictionary<string, object> ToDictionary()
        {
            var dict = new Dictionary<string, object>();
          WriteBaseProperties(dict);

    if (Diameter.HasValue) dict["xDiameter"] = Diameter.Value;
      if (Length.HasValue) dict["xLength"] = Length.Value;
            if (!string.IsNullOrEmpty(RebarMark)) dict["xRebarMark"] = RebarMark;
            if (!string.IsNullOrEmpty(RebarType)) dict["xRebarType"] = RebarType;
            if (!string.IsNullOrEmpty(SteelGrade)) dict["xSteelGrade"] = SteelGrade;
 if (Quantity.HasValue) dict["xQuantity"] = Quantity.Value;
            if (Spacing.HasValue) dict["xSpacing"] = Spacing.Value;

   return dict;
        }

        public override void FromDictionary(Dictionary<string, object> dict)
        {
            ReadBaseProperties(dict);

            if (dict.TryGetValue("xDiameter", out var d)) Diameter = ConvertToDouble(d);
            if (dict.TryGetValue("xLength", out var l)) Length = ConvertToDouble(l);
      if (dict.TryGetValue("xRebarMark", out var rm)) RebarMark = rm?.ToString();
            if (dict.TryGetValue("xRebarType", out var rt)) RebarType = rt?.ToString();
 if (dict.TryGetValue("xSteelGrade", out var sg)) SteelGrade = sg?.ToString();
 if (dict.TryGetValue("xQuantity", out var q)) Quantity = ConvertToInt(q);
 if (dict.TryGetValue("xSpacing", out var sp)) Spacing = ConvertToDouble(sp);
        }

     #endregion

        public void EnsureRebarMark()
        {
    if (string.IsNullOrEmpty(RebarMark) && Diameter.HasValue)
         {
     RebarMark = $"D{(int)Diameter.Value}";
  }
        }

        public override string ToString()
      {
            string markStr = RebarMark ?? $"D{Diameter ?? 0}";
   string qtyStr = Quantity.HasValue ? $"x{Quantity.Value}" : "";
   return $"Rebar[{markStr}] {RebarType} {SteelGrade}{qtyStr}";
        }
    }
}
