using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Windows;
using System;
using System.Drawing;

namespace DTS_Engine.UI.Forms
{
    /// <summary>
    /// DashboardPalette - Mini-Toolbar trôi nổi dạng Photoshop
    /// Hiển thị thanh công cụ ngang nhỏ gọn với các lệnh DTS phổ biến
    /// </summary>
    public static class DashboardPalette
    {
        private static PaletteSet _ps;
        private static DashboardControl _control;

        /// <summary>
        /// Hiện/Ẩn Dashboard Mini-Toolbar
        /// </summary>
        public static void ShowPalette()
        {
            if (_ps == null)
            {
                _ps = new PaletteSet("DTS Mini", new Guid("E8F3D5A1-7B2C-4D6E-9A8F-1C3B5D7E9F0A"));

                // Kích thước compact: 9 nút x 38px + padding + margins
                int width = 380;
                int height = 55;

                _ps.MinimumSize = new Size(width, height);
                _ps.Size = new Size(width, height);

                // Dock = None để trôi nổi tự do
                _ps.Dock = DockSides.None;

                // Tắt bớt nút menu thừa cho gọn
                _ps.Style = PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowAutoHideButton;

                _control = new DashboardControl();
                _ps.Add("Toolbar", _control);
            }

            _ps.Visible = !_ps.Visible;
        }

        /// <summary>
        /// Đóng Dashboard
        /// </summary>
        public static void ClosePalette()
        {
            if (_ps != null)
            {
                _ps.Visible = false;
            }
        }
    }
}
