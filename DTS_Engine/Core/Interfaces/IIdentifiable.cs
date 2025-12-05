namespace DTS_Engine.Core.Interfaces
{
    /// <summary>
    /// Interface cho các đối tượng có định danh
    /// </summary>
    public interface IIdentifiable
    {
        /// <summary>
        /// Handle trong AutoCAD
        /// </summary>
        string Handle { get; set; }

        /// <summary>
        /// ID duy nhất tính từ geometry
        /// </summary>
        string UniqueID { get; }

        /// <summary>
        /// Cập nhật UniqueID
        /// </summary>
        void UpdateUniqueID();
    }
}