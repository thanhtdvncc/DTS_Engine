using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using DTS_Engine.Core.Data;

namespace DTS_Engine.UI.Forms
{
    /// <summary>
    /// Cửa sổ cấu hình thông số cốt thép (Modern UI)
    /// </summary>
    public class RebarConfigDialog : Form
    {
        private WebView2 _webView;
        private RebarSettings _settings;
        private bool _isInitialized = false;

        public RebarConfigDialog()
        {
            _settings = RebarSettings.Instance;
            InitializeComponent();
            // Hook vào sự kiện Shown thay vì khởi tạo trong constructor
            this.Shown += RebarConfigDialog_Shown;
        }

        private void InitializeComponent()
        {
            // Tinh chỉnh Window cho chuyên nghiệp
            this.Text = "DTS Engine | Thiết lập thông số cốt thép";
            this.Size = new Size(680, 820); // Rộng hơn chút để thoáng
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog; // Cố định, không cho resize lung tung
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            _webView = new WebView2();
            _webView.Dock = DockStyle.Fill;
            this.Controls.Add(_webView);
        }

        private async void RebarConfigDialog_Shown(object sender, EventArgs e)
        {
            if (_isInitialized) return;
            _isInitialized = true;
            await InitializeWebViewAsync();
        }

        private async System.Threading.Tasks.Task InitializeWebViewAsync()
        {
            try 
            {
                // Kiểm tra form chưa bị đóng
                if (this.IsDisposed || _webView.IsDisposed) return;

                // Folder cache riêng để không xung đột quyền Admin
                string userDataFolder = Path.Combine(Path.GetTempPath(), "DTS_RebarConfig_Profile");
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);

                // Kiểm tra lại sau await vì form có thể đóng trong thời gian chờ
                if (this.IsDisposed || _webView.IsDisposed) return;

                await _webView.EnsureCoreWebView2Async(env);
                
                // Kiểm tra lại sau await
                if (this.IsDisposed || _webView.IsDisposed || _webView.CoreWebView2 == null) return;

                // Tắt menu chuột phải mặc định của trình duyệt (Inspect Element...) để nhìn như App Native
                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false; 
                _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;

                _webView.WebMessageReceived += WebView_WebMessageReceived;
                _webView.NavigateToString(GenerateHtml());
            }
            catch (ObjectDisposedException)
            {
                // Bỏ qua nếu form đã đóng
            }
            catch (Exception ex)
            {
                if (!this.IsDisposed)
                    MessageBox.Show("Lỗi khởi tạo giao diện: " + ex.Message);
            }
        }

        private void WebView_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string json = e.TryGetWebMessageAsString();
                
                if (json == "CANCEL")
                {
                    this.DialogResult = DialogResult.Cancel;
                    this.Close();
                    return;
                }

                JsonConvert.PopulateObject(json, _settings);
                
                // Feedback nhẹ nhàng thay vì MessageBox chặn màn hình
                // Nhưng vì đây là Dialog đóng luôn nên MessageBox OK là hợp lý
                //MessageBox.Show("Cập nhật cấu hình thành công!", "DTS System", MessageBoxButtons.OK, MessageBoxIcon.Information);
                
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Dữ liệu không hợp lệ: " + ex.Message, "System Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private string GenerateHtml()
        {
            string settingsJson = JsonConvert.SerializeObject(_settings);

            return $@"
<!DOCTYPE html>
<html lang='vi'>
<head>
    <meta charset='UTF-8'>
    <style>
        /* --- MODERN ENGINEERING THEME --- */
        body {{ font-family: 'Segoe UI', system-ui, sans-serif; background-color: #f8f9fa; padding: 0; margin: 0; color: #212529; user-select: none; }}
        
        /* Header cố định */
        .header {{ background: #ffffff; padding: 20px 30px; border-bottom: 1px solid #e9ecef; position: sticky; top: 0; z-index: 100; display: flex; align-items: center; justify-content: space-between; }}
        .header h2 {{ margin: 0; font-size: 1.25rem; color: #0d6efd; font-weight: 700; text-transform: uppercase; letter-spacing: 0.5px; }}
        .badge {{ background: #e7f1ff; color: #0d6efd; padding: 4px 10px; border-radius: 20px; font-size: 0.75rem; font-weight: 600; }}

        /* Nội dung cuộn */
        .content {{ padding: 25px 30px; padding-bottom: 80px; }}

        .card {{ background: white; border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,0.04); margin-bottom: 20px; overflow: hidden; border: 1px solid rgba(0,0,0,0.05); }}
        .card-header {{ background: #fcfcfc; padding: 12px 20px; border-bottom: 1px solid #f0f0f0; font-weight: 600; color: #495057; font-size: 0.95rem; display: flex; align-items: center; }}
        .card-header::before {{ content: ''; width: 4px; height: 16px; background: #0d6efd; margin-right: 10px; border-radius: 2px; }}
        .card-body {{ padding: 20px; }}

        .grid-2 {{ display: grid; grid-template-columns: 1fr 1fr; gap: 20px; }}
        .grid-3 {{ display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 20px; }}

        .form-group {{ margin-bottom: 0; }}
        label {{ display: block; font-size: 0.85rem; font-weight: 500; margin-bottom: 6px; color: #6c757d; }}
        input {{ width: 100%; padding: 10px 12px; border: 1px solid #ced4da; border-radius: 5px; font-size: 0.9rem; transition: all 0.2s; box-sizing: border-box; }}
        input:focus {{ border-color: #86b7fe; outline: none; box-shadow: 0 0 0 3px rgba(13, 110, 253, 0.15); }}
        
        .hint {{ font-size: 0.75rem; color: #adb5bd; margin-top: 4px; display: flex; align-items: center; gap: 4px; }}
        
        /* Footer nút bấm cố định */
        .footer {{ position: fixed; bottom: 0; left: 0; right: 0; background: white; padding: 15px 30px; border-top: 1px solid #e9ecef; display: flex; justify-content: flex-end; gap: 12px; z-index: 100; }}
        
        button {{ padding: 10px 24px; border: none; border-radius: 6px; font-weight: 600; font-size: 0.9rem; cursor: pointer; transition: 0.2s; }}
        .btn-primary {{ background-color: #0d6efd; color: white; box-shadow: 0 4px 6px rgba(13, 110, 253, 0.2); }}
        .btn-primary:hover {{ background-color: #0b5ed7; transform: translateY(-1px); }}
        .btn-primary:active {{ transform: translateY(0); }}
        .btn-secondary {{ background-color: #f8f9fa; color: #212529; border: 1px solid #dee2e6; }}
        .btn-secondary:hover {{ background-color: #e9ecef; }}

    </style>
</head>
<body>

    <div class='header'>
        <h2>DTS Configuration</h2>
        <span class='badge'>v1.0.2</span>
    </div>

    <div class='content'>
        <div class='card'>
            <div class='card-header'>Cài đặt vùng xét nội lực (Design Zones)</div>
            <div class='card-body'>
                <div class='grid-2'>
                    <div class='form-group'>
                        <label>Vùng đầu (Start Ratio)</label>
                        <input type='number' id='ZoneRatioStart' step='0.05' min='0' max='0.5'>
                        <div class='hint'>Tỷ lệ L (Ví dụ: 0.25)</div>
                    </div>
                    <div class='form-group'>
                        <label>Vùng cuối (End Ratio)</label>
                        <input type='number' id='ZoneRatioEnd' step='0.05' min='0' max='0.5'>
                        <div class='hint'>Tỷ lệ L (Ví dụ: 0.25)</div>
                    </div>
                </div>
            </div>
        </div>

        <div class='card'>
            <div class='card-header'>Hệ số phân bổ thép Xoắn (Torsion Factors)</div>
            <div class='card-body'>
                <div class='grid-3'>
                    <div class='form-group'>
                        <label>Top Factor</label>
                        <input type='number' id='TorsionFactorTop' step='0.05'>
                    </div>
                    <div class='form-group'>
                        <label>Bottom Factor</label>
                        <input type='number' id='TorsionFactorBot' step='0.05'>
                    </div>
                    <div class='form-group'>
                        <label>Side (Web) Factor</label>
                        <input type='number' id='TorsionFactorSide' step='0.05'>
                    </div>
                </div>
            </div>
        </div>

        <div class='card'>
            <div class='card-header'>Thông số Cốt thép (Reinforcement)</div>
            <div class='card-body'>
                <div class='form-group' style='margin-bottom: 15px;'>
                    <label>Danh sách đường kính dọc (Preferred Diameters)</label>
                    <input type='text' id='PreferredDiameters' placeholder='16, 18, 20...'>
                    <div class='hint'>⚠️ Nhập các số cách nhau bởi dấu phẩy</div>
                </div>

                <div class='grid-3'>
                    <div class='form-group'>
                        <label>ĐK Đai (Stirrup Ø)</label>
                        <input type='number' id='StirrupDiameter'>
                    </div>
                    <div class='form-group'>
                        <label>Số nhánh đai</label>
                        <input type='number' id='StirrupLegs'>
                    </div>
                    <div class='form-group'>
                        <label>ĐK Sườn (Web Ø)</label>
                        <input type='number' id='WebBarDiameter'>
                    </div>
                </div>
            </div>
        </div>

         <div class='card'>
            <div class='card-header'>Bê tông & Bảo vệ (Cover)</div>
            <div class='card-body'>
                <div class='grid-2'>
                    <div class='form-group'>
                        <label>Lớp bảo vệ Top (mm)</label>
                        <input type='number' id='CoverTop'>
                    </div>
                    <div class='form-group'>
                        <label>Lớp bảo vệ Bot (mm)</label>
                        <input type='number' id='CoverBot'>
                    </div>
                </div>
                <div class='grid-2' style='margin-top: 15px;'>
                    <div class='form-group'>
                        <label>Khoảng hở cốt liệu (mm)</label>
                        <input type='number' id='MinSpacing'>
                    </div>
                    <div class='form-group'>
                        <label>Chiều cao dầm tối thiểu có sườn</label>
                        <input type='number' id='WebBarMinHeight'>
                    </div>
                </div>
            </div>
        </div>
    </div>

    <div class='footer'>
        <button class='btn-secondary' onclick='doCancel()'>Hủy bỏ</button>
        <button class='btn-primary' onclick='doSave()'>Lưu cấu hình</button>
    </div>

<script>
    const data = {settingsJson};

    function loadForm() {{
        const setId = (id, val) => document.getElementById(id).value = val;
        
        setId('ZoneRatioStart', data.ZoneRatioStart);
        setId('ZoneRatioEnd', data.ZoneRatioEnd);
        setId('TorsionFactorTop', data.TorsionFactorTop);
        setId('TorsionFactorBot', data.TorsionFactorBot);
        setId('TorsionFactorSide', data.TorsionFactorSide);
        setId('CoverTop', data.CoverTop);
        setId('CoverBot', data.CoverBot);
        setId('StirrupDiameter', data.StirrupDiameter);
        setId('StirrupLegs', data.StirrupLegs);
        setId('WebBarDiameter', data.WebBarDiameter);
        setId('MinSpacing', data.MinSpacing);
        setId('WebBarMinHeight', data.WebBarMinHeight);

        if(data.PreferredDiameters) {{
            setId('PreferredDiameters', data.PreferredDiameters.join(', '));
        }}
    }}

    function doSave() {{
        const getVal = (id) => {{
            const el = document.getElementById(id);
            return el.type === 'number' ? parseFloat(el.value) : el.value;
        }};

        const result = {{
            ZoneRatioStart: getVal('ZoneRatioStart'),
            ZoneRatioEnd: getVal('ZoneRatioEnd'),
            TorsionFactorTop: getVal('TorsionFactorTop'),
            TorsionFactorBot: getVal('TorsionFactorBot'),
            TorsionFactorSide: getVal('TorsionFactorSide'),
            CoverTop: getVal('CoverTop'),
            CoverBot: getVal('CoverBot'),
            StirrupDiameter: parseInt(getVal('StirrupDiameter')),
            StirrupLegs: parseInt(getVal('StirrupLegs')),
            WebBarDiameter: parseInt(getVal('WebBarDiameter')),
            MinSpacing: getVal('MinSpacing'),
            WebBarMinHeight: getVal('WebBarMinHeight'),
            PreferredDiameters: getVal('PreferredDiameters').split(',').map(s => parseInt(s.trim())).filter(n => !isNaN(n))
        }};

        window.chrome.webview.postMessage(JSON.stringify(result));
    }}

    function doCancel() {{ window.chrome.webview.postMessage('CANCEL'); }}
    
    // Auto load
    loadForm();
</script>
</body>
</html>";
        }
    }
}
