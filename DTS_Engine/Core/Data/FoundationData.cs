using System.Collections.Generic;

namespace DTS_Wall_Tool.Core.Data
{
    /// <summary>
    /// D? li?u Móng - K? th?a t? ElementData.
    /// </summary>
    public class FoundationData : ElementData
    {
        #region Identity Override

        public override ElementType ElementType => ElementType.Foundation;

        #endregion

        #region Foundation-Specific Properties

        /// <summary>
        /// Chi?u r?ng móng (mm)
        /// </summary>
    public double? Width { get; set; } = null;

        /// <summary>
        /// Chi?u dài móng (mm)
        /// </summary>
 public double? Length { get; set; } = null;

        /// <summary>
        /// Chi?u sâu móng (mm)
        /// </summary>
 public double? Depth { get; set; } = null;

        /// <summary>
        /// Lo?i móng (Strip, Pad, Raft...)
        /// </summary>
      public string FoundationType { get; set; } = "Strip";

        /// <summary>
        /// V?t li?u
   /// </summary>
        public string Material { get; set; } = "Concrete";

        #endregion

        #region Override Methods

   public override bool HasValidData()
        {
       return Width.HasValue || Length.HasValue || Depth.HasValue;
     }

        public override ElementData Clone()
        {
            var clone = new FoundationData
       {
       Width = Width,
                Length = Length,
    Depth = Depth,
         FoundationType = FoundationType,
        Material = Material
            };

            CopyBaseTo(clone);
      return clone;
        }

        public override Dictionary<string, object> ToDictionary()
    {
            var dict = new Dictionary<string, object>();
            WriteBaseProperties(dict);

  if (Width.HasValue) dict["xWidth"] = Width.Value;
            if (Length.HasValue) dict["xLength"] = Length.Value;
         if (Depth.HasValue) dict["xDepth"] = Depth.Value;
         if (!string.IsNullOrEmpty(FoundationType)) dict["xFoundationType"] = FoundationType;
            if (!string.IsNullOrEmpty(Material)) dict["xMaterial"] = Material;

            return dict;
    }

        public override void FromDictionary(Dictionary<string, object> dict)
    {
   ReadBaseProperties(dict);

            if (dict.TryGetValue("xWidth", out var w)) Width = ConvertToDouble(w);
            if (dict.TryGetValue("xLength", out var l)) Length = ConvertToDouble(l);
       if (dict.TryGetValue("xDepth", out var d)) Depth = ConvertToDouble(d);
      if (dict.TryGetValue("xFoundationType", out var ft)) FoundationType = ft?.ToString();
            if (dict.TryGetValue("xMaterial", out var mat)) Material = mat?.ToString();
        }

        #endregion

  public override string ToString()
        {
            return $"Foundation[{FoundationType}] {Width}x{Length}x{Depth}";
}
    }
}
