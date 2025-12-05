using System.Collections.Generic;

namespace DTS_Wall_Tool.Core.Data
{
    /// <summary>
    /// D? li?u C?u thang - K? th?a t? ElementData.
 /// </summary>
    public class StairData : ElementData
  {
   #region Identity Override

        public override ElementType ElementType => ElementType.Stair;

   #endregion

   #region Stair-Specific Properties

    /// <summary>
        /// Chi?u r?ng b?c thang (mm)
        /// </summary>
        public double? Width { get; set; } = null;

        /// <summary>
        /// Chi?u dài b?c thang (mm)
     /// </summary>
  public double? Length { get; set; } = null;

        /// <summary>
      /// Chi?u cao b?c (mm)
        /// </summary>
        public double? Riser { get; set; } = null;

      /// <summary>
    /// Chi?u r?ng m?t b?c (mm)
        /// </summary>
        public double? Tread { get; set; } = null;

        /// <summary>
        /// S? b?c
        /// </summary>
     public int? NumberOfSteps { get; set; } = null;

        /// <summary>
        /// Lo?i c?u thang
        /// </summary>
        public string StairType { get; set; } = "Straight";

        /// <summary>
        /// V?t li?u
        /// </summary>
        public string Material { get; set; } = "Concrete";

        #endregion

      #region Override Methods

        public override bool HasValidData()
        {
      return Width.HasValue || NumberOfSteps.HasValue;
        }

        public override ElementData Clone()
        {
            var clone = new StairData
 {
          Width = Width,
             Length = Length,
      Riser = Riser,
             Tread = Tread,
            NumberOfSteps = NumberOfSteps,
     StairType = StairType,
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
   if (Riser.HasValue) dict["xRiser"] = Riser.Value;
   if (Tread.HasValue) dict["xTread"] = Tread.Value;
      if (NumberOfSteps.HasValue) dict["xNumberOfSteps"] = NumberOfSteps.Value;
     if (!string.IsNullOrEmpty(StairType)) dict["xStairType"] = StairType;
            if (!string.IsNullOrEmpty(Material)) dict["xMaterial"] = Material;

            return dict;
      }

      public override void FromDictionary(Dictionary<string, object> dict)
        {
 ReadBaseProperties(dict);

     if (dict.TryGetValue("xWidth", out var w)) Width = ConvertToDouble(w);
            if (dict.TryGetValue("xLength", out var l)) Length = ConvertToDouble(l);
if (dict.TryGetValue("xRiser", out var r)) Riser = ConvertToDouble(r);
        if (dict.TryGetValue("xTread", out var t)) Tread = ConvertToDouble(t);
      if (dict.TryGetValue("xNumberOfSteps", out var nos)) NumberOfSteps = ConvertToInt(nos);
    if (dict.TryGetValue("xStairType", out var st)) StairType = st?.ToString();
         if (dict.TryGetValue("xMaterial", out var mat)) Material = mat?.ToString();
  }

   #endregion

        public override string ToString()
 {
       return $"Stair[{StairType}] {NumberOfSteps} steps";
        }
    }
}
