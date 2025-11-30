namespace DTS_Wall_Tool.Core.Interfaces
{
    /// <summary>
    /// Interface cho các đối tượng có trạng thái active/inactive
    /// </summary>
    public interface IActivatable
    {
        /// <summary>
        /// Đang hoạt động (chưa bị xóa/vô hiệu hóa)
        /// </summary>
        bool IsActive { get; set; }
    }
}