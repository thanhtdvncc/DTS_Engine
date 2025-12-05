using System;

namespace DTS_Wall_Tool.Core.Data
{
    /// <summary>
    /// Trạng thái đồng bộ giữa CAD và SAP2000
    /// </summary>
    public enum SyncState
    {
        /// <summary>Chưa đồng bộ</summary>
        NotSynced = 0,

        /// <summary>Đã đồng bộ, không có thay đổi</summary>
        Synced = 1,

        /// <summary>CAD đã thay đổi, cần đẩy lên SAP</summary>
        CadModified = 2,

        /// <summary>SAP đã thay đổi, cần cập nhật CAD</summary>
        SapModified = 3,

        /// <summary>Xung đột - cả 2 bên đều thay đổi</summary>
        Conflict = 4,

        /// <summary>Phần tử SAP không còn tồn tại</summary>
        SapDeleted = 5,

        /// <summary>Phần tử mới, chưa có trong SAP</summary>
        NewElement = 6
    }

    /// <summary>
    /// Thông tin đồng bộ chi tiết
    /// </summary>
    public class SyncInfo
    {
        /// <summary>
        /// Trạng thái đồng bộ hiện tại
        /// </summary>
        public SyncState State { get; set; } = SyncState.NotSynced;

        /// <summary>
        /// Thời điểm đồng bộ cuối từ SAP (UTC)
        /// </summary>
        public DateTime? LastSyncFromSap { get; set; }

        /// <summary>
        /// Thời điểm đồng bộ cuối sang SAP (UTC)
        /// </summary>
        public DateTime? LastSyncToSap { get; set; }

        /// <summary>
        /// Hash của dữ liệu SAP để phát hiện thay đổi
        /// </summary>
        public string SapDataHash { get; set; }

        /// <summary>
        /// Hash của dữ liệu CAD để phát hiện thay đổi
        /// </summary>
        public string CadDataHash { get; set; }

        /// <summary>
        /// Thông tin tải trọng từ SAP (cache)
        /// </summary>
        public SapLoadInfo SapLoadCache { get; set; }

        public override string ToString()
        {
            return $"Sync[{State}] LastSync: {LastSyncFromSap:yyyy-MM-dd HH:mm}";
        }
    }

    /// <summary>
    /// Cache thông tin tải trọng từ SAP2000
    /// </summary>
    public class SapLoadInfo
    {
        /// <summary>Tên frame trong SAP</summary>
        public string FrameName { get; set; }

        /// <summary>Load Pattern</summary>
        public string LoadPattern { get; set; }

        /// <summary>Giá trị tải (kN/m)</summary>
        public double LoadValue { get; set; }

        /// <summary>Vị trí bắt đầu tải (mm)</summary>
        public double DistanceI { get; set; }

        /// <summary>Vị trí kết thúc tải (mm)</summary>
        public double DistanceJ { get; set; }

        /// <summary>Hướng tải (Gravity, X, Y... )</summary>
        public string Direction { get; set; } = "Gravity";

        /// <summary>Loại tải (Distributed, Point... )</summary>
        public string LoadType { get; set; } = "Distributed";

        public override string ToString()
        {
            return $"{FrameName}: {LoadPattern}={LoadValue:0. 00}kN/m [{DistanceI:0}-{DistanceJ:0}]";
        }
    }
}