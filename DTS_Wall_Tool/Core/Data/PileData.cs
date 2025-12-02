using System.Collections.Generic;

namespace DTS_Wall_Tool.Core.Data
{
    /// <summary>
    /// D? li?u C?c - K? th?a t? ElementData.
    /// </summary>
 public class PileData : ElementData
{
     #region Identity Override

        public override ElementType ElementType => ElementType.Pile;

        #endregion

        #region Pile-Specific Properties

        /// <summary>
        /// ???ng kính c?c (mm)
  /// </summary>
        public double? Diameter { get; set; } = null;

        /// <summary>
      /// Chi?u dài c?c (mm)
        /// </summary>
        public double? Length { get; set; } = null;

  /// <summary>
        /// Lo?i c?c (VD: "D300", "D400")
        /// </summary>
        public string PileType { get; set; } = null;

        /// <summary>
 /// Ph??ng pháp thi công (Bored, Driven...)
   /// </summary>
        public string ConstructionMethod { get; set; } = "Bored";

        /// <summary>
        /// V?t li?u
        /// </summary>
  public string Material { get; set; } = "Concrete";

        /// <summary>
        /// Mác bê tông
        /// </summary>
        public string ConcreteGrade { get; set; } = "C30";

        /// <summary>
        /// S?c ch?u t?i (kN)
   /// </summary>
        public double? BearingCapacity { get; set; } = null;

        #endregion

     #region Override Methods

        public override bool HasValidData()
        {
            return Diameter.HasValue || !string.IsNullOrEmpty(PileType);
  }

        public override ElementData Clone()
        {
            var clone = new PileData
        {
 Diameter = Diameter,
Length = Length,
        PileType = PileType,
     ConstructionMethod = ConstructionMethod,
  Material = Material,
                ConcreteGrade = ConcreteGrade,
                BearingCapacity = BearingCapacity
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
       if (!string.IsNullOrEmpty(PileType)) dict["xPileType"] = PileType;
            if (!string.IsNullOrEmpty(ConstructionMethod)) dict["xConstructionMethod"] = ConstructionMethod;
            if (!string.IsNullOrEmpty(Material)) dict["xMaterial"] = Material;
            if (!string.IsNullOrEmpty(ConcreteGrade)) dict["xConcreteGrade"] = ConcreteGrade;
            if (BearingCapacity.HasValue) dict["xBearingCapacity"] = BearingCapacity.Value;

            return dict;
     }

        public override void FromDictionary(Dictionary<string, object> dict)
        {
            ReadBaseProperties(dict);

    if (dict.TryGetValue("xDiameter", out var d)) Diameter = ConvertToDouble(d);
       if (dict.TryGetValue("xLength", out var l)) Length = ConvertToDouble(l);
  if (dict.TryGetValue("xPileType", out var pt)) PileType = pt?.ToString();
     if (dict.TryGetValue("xConstructionMethod", out var cm)) ConstructionMethod = cm?.ToString();
      if (dict.TryGetValue("xMaterial", out var mat)) Material = mat?.ToString();
            if (dict.TryGetValue("xConcreteGrade", out var cg)) ConcreteGrade = cg?.ToString();
 if (dict.TryGetValue("xBearingCapacity", out var bc)) BearingCapacity = ConvertToDouble(bc);
        }

        #endregion

     public override string ToString()
        {
   string typeStr = PileType ?? $"D{Diameter ?? 0}";
            return $"Pile[{typeStr}] L={Length ?? 0}mm";
     }
    }
}
