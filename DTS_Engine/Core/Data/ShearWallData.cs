using System.Collections.Generic;

namespace DTS_Wall_Tool.Core.Data
{
    /// <summary>
    /// D? li?u Vách ch?u l?c - K? th?a t? ElementData.
    /// </summary>
    public class ShearWallData : ElementData
    {
        #region Identity Override

        public override ElementType ElementType => ElementType.ShearWall;

        #endregion

        #region ShearWall-Specific Properties

      /// <summary>
 /// ?? dày vách (mm)
        /// </summary>
        public double? Thickness { get; set; } = null;

        /// <summary>
        /// Chi?u dài vách (mm)
        /// </summary>
        public double? Length { get; set; } = null;

   /// <summary>
        /// Lo?i vách (VD: "SW300", "SW250")
        /// </summary>
  public string WallType { get; set; } = null;

      /// <summary>
   /// V?t li?u
     /// </summary>
        public string Material { get; set; } = "Concrete";

        /// <summary>
        /// Mác bê tông
        /// </summary>
        public string ConcreteGrade { get; set; } = "C30";

        #endregion

        #region Override Methods

        public override bool HasValidData()
        {
            return Thickness.HasValue || !string.IsNullOrEmpty(WallType);
        }

        public override ElementData Clone()
        {
            var clone = new ShearWallData
       {
   Thickness = Thickness,
    Length = Length,
     WallType = WallType,
   Material = Material,
      ConcreteGrade = ConcreteGrade
       };

        CopyBaseTo(clone);
            return clone;
   }

        public override Dictionary<string, object> ToDictionary()
    {
         var dict = new Dictionary<string, object>();
            WriteBaseProperties(dict);

      if (Thickness.HasValue) dict["xThickness"] = Thickness.Value;
            if (Length.HasValue) dict["xLength"] = Length.Value;
if (!string.IsNullOrEmpty(WallType)) dict["xWallType"] = WallType;
   if (!string.IsNullOrEmpty(Material)) dict["xMaterial"] = Material;
            if (!string.IsNullOrEmpty(ConcreteGrade)) dict["xConcreteGrade"] = ConcreteGrade;

    return dict;
        }

   public override void FromDictionary(Dictionary<string, object> dict)
        {
      ReadBaseProperties(dict);

   if (dict.TryGetValue("xThickness", out var t)) Thickness = ConvertToDouble(t);
            if (dict.TryGetValue("xLength", out var l)) Length = ConvertToDouble(l);
   if (dict.TryGetValue("xWallType", out var wt)) WallType = wt?.ToString();
if (dict.TryGetValue("xMaterial", out var mat)) Material = mat?.ToString();
      if (dict.TryGetValue("xConcreteGrade", out var cg)) ConcreteGrade = cg?.ToString();
        }

        #endregion

        public override string ToString()
    {
            string typeStr = WallType ?? $"SW{Thickness ?? 0}";
   return $"ShearWall[{typeStr}] {Material}";
        }
    }
}
