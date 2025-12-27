using System;
using System.Drawing;
using System.Windows.Forms;
using DTS_Engine.Core.Data;

namespace DTS_Engine.UI.Controls
{
    /// <summary>
    /// Giao diện cấu hình bản vẽ mặt cắt dầm.
    /// Cho phép tùy chỉnh Layer, Màu sắc, Lớp bảo vệ và Chiều cao chữ.
    /// </summary>
    public class DrawingSettingsControl : UserControl
    {
        private GroupBox grpConcrete;
        private GroupBox grpRebar;
        private GroupBox grpStirrup;
        private GroupBox grpAnnotation;

        private TextBox txtLayerConc, txtLayerRebar, txtLayerStirrup, txtLayerSide, txtLayerDim, txtLayerText;
        private ComboBox cbColorConc, cbColorRebar, cbColorStirrup, cbColorSide, cbColorDim, cbColorText;
        private NumericUpDown numCover, numTextHeight, numMaxHeight, numDimScale;
        private CheckBox chkDrawHook;

        public DrawingSettingsControl()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void InitializeComponent()
        {
            this.Size = new Size(500, 650);
            this.AutoScroll = true;

            int yPos = 10;
            grpConcrete = CreateGroup("Bê tông", ref yPos);
            AddConfigRow(grpConcrete, "Layer:", ref txtLayerConc, 0);
            AddColorRow(grpConcrete, "Màu:", ref cbColorConc, 1);
            AddNumericRow(grpConcrete, "Lớp bảo vệ (mm):", ref numCover, 2, 25);

            grpRebar = CreateGroup("Thép chủ & Thép sườn", ref yPos);
            AddConfigRow(grpRebar, "Layer Thép chủ:", ref txtLayerRebar, 0);
            AddColorRow(grpRebar, "Màu Thép chủ:", ref cbColorRebar, 1);
            AddConfigRow(grpRebar, "Layer Thép sườn:", ref txtLayerSide, 2);
            AddColorRow(grpRebar, "Màu Thép sườn:", ref cbColorSide, 3);
            grpRebar.Height += 50;
            yPos += 50;

            grpStirrup = CreateGroup("Thép đai", ref yPos);
            AddConfigRow(grpStirrup, "Layer:", ref txtLayerStirrup, 0);
            AddColorRow(grpStirrup, "Màu:", ref cbColorStirrup, 1);
            chkDrawHook = new CheckBox { Text = "Vẽ móc 135 độ", Location = new Point(20, 85), Checked = true, AutoSize = true };
            grpStirrup.Controls.Add(chkDrawHook);

            grpAnnotation = CreateGroup("Ghi chú & Kích thước", ref yPos);
            AddConfigRow(grpAnnotation, "Layer Dim:", ref txtLayerDim, 0);
            AddColorRow(grpAnnotation, "Màu Dim:", ref cbColorDim, 1);
            AddConfigRow(grpAnnotation, "Layer Text:", ref txtLayerText, 2);
            AddColorRow(grpAnnotation, "Màu Text:", ref cbColorText, 3);
            AddNumericRow(grpAnnotation, "Chiều cao chữ (mm):", ref numTextHeight, 4, 250);
            AddNumericRow(grpAnnotation, "Tỷ lệ Dim (Scale):", ref numDimScale, 5, 1.0);
            AddNumericRow(grpAnnotation, "Chiều cao bảng tối đa (m):", ref numMaxHeight, 6, 15);
            grpAnnotation.Height += 80;
        }

        private GroupBox CreateGroup(string title, ref int y)
        {
            var g = new GroupBox { Text = title, Location = new Point(10, y), Size = new Size(460, 130) };
            this.Controls.Add(g);
            y += 140;
            return g;
        }

        private void AddConfigRow(GroupBox g, string label, ref TextBox txt, int row)
        {
            g.Controls.Add(new Label { Text = label, Location = new Point(20, 25 + row * 28), AutoSize = true });
            txt = new TextBox { Location = new Point(180, 22 + row * 28), Width = 200 };
            g.Controls.Add(txt);
        }

        private void AddColorRow(GroupBox g, string label, ref ComboBox cb, int row)
        {
            g.Controls.Add(new Label { Text = label, Location = new Point(20, 25 + row * 28), AutoSize = true });
            cb = new ComboBox { Location = new Point(180, 22 + row * 28), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            cb.Items.AddRange(new object[] { "1 - Red", "2 - Yellow", "3 - Green", "4 - Cyan", "5 - Blue", "6 - Magenta", "7 - White", "8 - Gray" });
            g.Controls.Add(cb);
        }

        private void AddNumericRow(GroupBox g, string label, ref NumericUpDown num, int row, double defVal)
        {
            g.Controls.Add(new Label { Text = label, Location = new Point(20, 25 + row * 28), AutoSize = true });
            num = new NumericUpDown { Location = new Point(180, 22 + row * 28), Width = 100, Maximum = 100000, DecimalPlaces = 1, Value = (decimal)defVal };
            g.Controls.Add(num);
        }

        public void LoadSettings()
        {
            var s = DtsSettings.Instance.Drawing;
            txtLayerConc.Text = s.LayerConcrete;
            cbColorConc.SelectedIndex = Math.Max(0, Math.Min(7, s.ColorConcrete - 1));
            numCover.Value = (decimal)s.ConcreteCover;

            txtLayerRebar.Text = s.LayerMainRebar;
            cbColorRebar.SelectedIndex = Math.Max(0, Math.Min(7, s.ColorMainRebar - 1));
            txtLayerSide.Text = s.LayerSideBar;
            cbColorSide.SelectedIndex = Math.Max(0, Math.Min(7, s.ColorSideBar - 1));

            txtLayerStirrup.Text = s.LayerStirrup;
            cbColorStirrup.SelectedIndex = Math.Max(0, Math.Min(7, s.ColorStirrup - 1));
            chkDrawHook.Checked = s.DrawStirrupHook;

            txtLayerDim.Text = s.LayerDim;
            cbColorDim.SelectedIndex = Math.Max(0, Math.Min(7, s.ColorDim - 1));
            txtLayerText.Text = s.LayerText;
            cbColorText.SelectedIndex = Math.Max(0, Math.Min(7, s.ColorText - 1));
            numTextHeight.Value = (decimal)s.TextHeight;
            numDimScale.Value = (decimal)s.DimScale;
            numMaxHeight.Value = (decimal)(s.MaxTableHeight / 1000.0); // Hiển thị mét cho dễ dùng
        }

        public void SaveSettings()
        {
            var s = DtsSettings.Instance.Drawing;
            s.LayerConcrete = txtLayerConc.Text;
            s.ColorConcrete = cbColorConc.SelectedIndex + 1;
            s.ConcreteCover = (double)numCover.Value;

            s.LayerMainRebar = txtLayerRebar.Text;
            s.ColorMainRebar = cbColorRebar.SelectedIndex + 1;
            s.LayerSideBar = txtLayerSide.Text;
            s.ColorSideBar = cbColorSide.SelectedIndex + 1;

            s.LayerStirrup = txtLayerStirrup.Text;
            s.ColorStirrup = cbColorStirrup.SelectedIndex + 1;
            s.DrawStirrupHook = chkDrawHook.Checked;

            s.LayerDim = txtLayerDim.Text;
            s.ColorDim = cbColorDim.SelectedIndex + 1;
            s.LayerText = txtLayerText.Text;
            s.ColorText = cbColorText.SelectedIndex + 1;
            s.TextHeight = (double)numTextHeight.Value;
            s.DimScale = (double)numDimScale.Value;
            s.MaxTableHeight = (double)numMaxHeight.Value * 1000.0;
        }
    }
}
