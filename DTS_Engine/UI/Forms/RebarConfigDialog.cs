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
    /// Cửa sổ cấu hình thông số cốt thép (Modern UI - 2 Tabs)
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
            this.Shown += RebarConfigDialog_Shown;
        }

        private void InitializeComponent()
        {
            this.Text = "DTS Engine | Cấu hình Cốt thép";
            this.Size = new Size(620, 580); // Compact size
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
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
                if (this.IsDisposed || _webView.IsDisposed) return;

                string userDataFolder = Path.Combine(Path.GetTempPath(), "DTS_RebarConfig_Profile");
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);

                if (this.IsDisposed || _webView.IsDisposed) return;

                await _webView.EnsureCoreWebView2Async(env);
                
                if (this.IsDisposed || _webView.IsDisposed || _webView.CoreWebView2 == null) return;

                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false; 
                _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;

                _webView.WebMessageReceived += WebView_WebMessageReceived;
                _webView.NavigateToString(GenerateHtml());
            }
            catch (ObjectDisposedException) { }
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

                // Debug: Uncomment để xem JSON nhận được
                // MessageBox.Show("JSON nhận: " + json.Substring(0, Math.Min(500, json.Length)), "Debug");

                // PopulateObject update trực tiếp vào _settings (là Singleton Instance)
                var serSettings = new JsonSerializerSettings 
                { 
                    ObjectCreationHandling = ObjectCreationHandling.Replace // Replace lists thay vì merge
                };
                JsonConvert.PopulateObject(json, _settings, serSettings);
                
                // Debug: Confirm save worked
                System.Diagnostics.Debug.WriteLine($"[RebarSettings] Saved: ZoneStart={_settings.ZoneRatioStart}, CoverTop={_settings.CoverTop}");
                
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Dữ liệu không hợp lệ: " + ex.Message + "\n\nStack: " + ex.StackTrace, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        * {{ box-sizing: border-box; }}
        body {{ font-family: 'Segoe UI', sans-serif; background: #f5f5f5; margin: 0; padding: 0; color: #333; user-select: none; font-size: 13px; }}
        
        .header {{ background: linear-gradient(135deg, #0d6efd 0%, #0a58ca 100%); color: white; padding: 12px 20px; display: flex; justify-content: space-between; align-items: center; }}
        .header h2 {{ margin: 0; font-size: 15px; font-weight: 600; }}
        .badge {{ background: rgba(255,255,255,0.2); padding: 3px 10px; border-radius: 12px; font-size: 11px; }}

        .tabs {{ display: flex; background: #fff; border-bottom: 1px solid #dee2e6; }}
        .tab {{ flex: 1; padding: 10px; text-align: center; cursor: pointer; border-bottom: 2px solid transparent; font-weight: 500; color: #6c757d; transition: 0.2s; }}
        .tab:hover {{ background: #f8f9fa; }}
        .tab.active {{ color: #0d6efd; border-bottom-color: #0d6efd; }}

        .content {{ padding: 15px; height: 395px; overflow-y: auto; }}
        .tab-panel {{ display: none; }}
        .tab-panel.active {{ display: block; }}

        .section {{ background: white; border-radius: 6px; margin-bottom: 12px; box-shadow: 0 1px 3px rgba(0,0,0,0.08); }}
        .section-header {{ padding: 8px 12px; font-weight: 600; color: #495057; font-size: 12px; border-bottom: 1px solid #f0f0f0; display: flex; align-items: center; gap: 6px; }}
        .section-header::before {{ content: ''; width: 3px; height: 12px; background: #0d6efd; border-radius: 2px; }}
        .section-body {{ padding: 12px; }}

        .row {{ display: flex; gap: 10px; margin-bottom: 10px; }}
        .row:last-child {{ margin-bottom: 0; }}
        .col {{ flex: 1; }}
        .col-2 {{ flex: 2; }}

        label {{ display: block; font-size: 11px; color: #6c757d; margin-bottom: 4px; }}
        input[type='text'], input[type='number'] {{ width: 100%; padding: 7px 10px; border: 1px solid #ced4da; border-radius: 4px; font-size: 12px; }}
        input:focus {{ border-color: #86b7fe; outline: none; box-shadow: 0 0 0 2px rgba(13,110,253,0.15); }}

        .check-row {{ display: flex; align-items: center; gap: 8px; padding: 6px 0; }}
        .check-row input[type='checkbox'] {{ width: 16px; height: 16px; accent-color: #0d6efd; }}
        .check-row label {{ margin: 0; font-size: 12px; color: #333; }}

        .hint {{ font-size: 10px; color: #adb5bd; margin-top: 3px; }}

        .footer {{ position: fixed; bottom: 0; left: 0; right: 0; background: white; padding: 12px 20px; border-top: 1px solid #e9ecef; display: flex; justify-content: flex-end; gap: 10px; }}
        button {{ padding: 8px 20px; border: none; border-radius: 5px; font-weight: 600; font-size: 12px; cursor: pointer; }}
        .btn-primary {{ background: #0d6efd; color: white; }}
        .btn-primary:hover {{ background: #0b5ed7; }}
        .btn-secondary {{ background: #f8f9fa; border: 1px solid #dee2e6; }}
        .btn-secondary:hover {{ background: #e9ecef; }}
    </style>
</head>
<body>
    <div class='header'>
        <h2>DTS Rebar Configuration</h2>
        <span class='badge'>v1.1</span>
    </div>

    <div class='tabs'>
        <div class='tab active' onclick='showTab(0)'>Thông số Rebar</div>
        <div class='tab' onclick='showTab(1)'>Đặt tên Dầm</div>
    </div>

    <div class='content'>
        <!-- TAB 1: Thông số Rebar -->
        <div class='tab-panel active' id='tab0'>
            <div class='section'>
                <div class='section-header'>Phân vùng & Xoắn</div>
                <div class='section-body'>
                    <div class='row'>
                        <div class='col'><label>Zone Start</label><input type='number' id='ZoneRatioStart' step='0.05'></div>
                        <div class='col'><label>Zone End</label><input type='number' id='ZoneRatioEnd' step='0.05'></div>
                        <div class='col'><label>Xoắn Top</label><input type='number' id='TorsionFactorTop' step='0.05'></div>
                        <div class='col'><label>Xoắn Bot</label><input type='number' id='TorsionFactorBot' step='0.05'></div>
                        <div class='col'><label>Xoắn Side</label><input type='number' id='TorsionFactorSide' step='0.05'></div>
                    </div>
                </div>
            </div>

            <div class='section'>
                <div class='section-header'>Lớp bảo vệ (mm)</div>
                <div class='section-body'>
                    <div class='row'>
                        <div class='col'><label>Cover Top</label><input type='number' id='CoverTop'></div>
                        <div class='col'><label>Cover Bot</label><input type='number' id='CoverBot'></div>
                        <div class='col'><label>Cover Side</label><input type='number' id='CoverSide'></div>
                        <div class='col'><label>Khoảng hở</label><input type='number' id='MinSpacing'></div>
                    </div>
                </div>
            </div>

            <div class='section'>
                <div class='section-header'>Thép dọc</div>
                <div class='section-body'>
                    <div class='row'>
                        <div class='col-2'>
                            <label>Đường kính ưu tiên</label>
                            <input type='text' id='PreferredDiameters' placeholder='16, 18, 20, 22, 25'>
                        </div>
                    </div>
                </div>
            </div>

            <div class='section'>
                <div class='section-header'>Thép đai</div>
                <div class='section-body'>
                    <div class='row'>
                        <div class='col-2'>
                            <label>Đường kính đai</label>
                            <input type='text' id='StirrupDiameters' placeholder='8, 10'>
                        </div>
                    </div>
                    <div class='check-row'>
                        <input type='checkbox' id='AllowOddLegs'>
                        <label for='AllowOddLegs'>Cho phép nhánh lẻ (3, 5...)</label>
                    </div>
                    <div class='check-row'>
                        <input type='checkbox' id='AutoLegsFromWidth'>
                        <label for='AutoLegsFromWidth'>Tự động số nhánh theo bề rộng</label>
                    </div>
                    <div class='row'>
                        <div class='col-2'>
                            <label>Quy tắc (bề rộng-nhánh)</label>
                            <input type='text' id='AutoLegsRules' placeholder='250-2 400-3 600-4'>
                            <div class='hint'>VD: 250-2 nghĩa là b≤250mm → 2 nhánh</div>
                        </div>
                    </div>
                </div>
            </div>

            <div class='section'>
                <div class='section-header'>Thép sườn</div>
                <div class='section-body'>
                    <div class='row'>
                        <div class='col'>
                            <label>Đường kính sườn</label>
                            <input type='text' id='WebBarDiameters' placeholder='12, 14'>
                        </div>
                        <div class='col'>
                            <label>H tối thiểu (mm)</label>
                            <input type='number' id='WebBarMinHeight'>
                        </div>
                    </div>
                </div>
            </div>
        </div>

        <!-- TAB 2: Đặt tên Dầm -->
        <div class='tab-panel' id='tab1'>
            <div class='section'>
                <div class='section-header'>Tiền tố / Hậu tố</div>
                <div class='section-body'>
                    <div class='row'>
                        <div class='col'><label>Beam Prefix</label><input type='text' id='BeamPrefix'></div>
                        <div class='col'><label>Girder Prefix</label><input type='text' id='GirderPrefix'></div>
                        <div class='col'><label>Suffix</label><input type='text' id='BeamSuffix'></div>
                    </div>
                </div>
            </div>

            <div class='section'>
                <div class='section-header'>Quy tắc nhóm</div>
                <div class='section-body'>
                    <div class='check-row'>
                        <input type='checkbox' id='GroupByAxis'>
                        <label for='GroupByAxis'>Nhóm theo trục (A1, B2...)</label>
                    </div>
                    <div class='check-row'>
                        <input type='checkbox' id='MergeSameSection'>
                        <label for='MergeSameSection'>Gộp dầm cùng section & rebar thành 1 tên</label>
                    </div>
                    <div class='check-row'>
                        <input type='checkbox' id='AutoRenameOnSectionChange'>
                        <label for='AutoRenameOnSectionChange'>Tự động rename khi section thay đổi</label>
                    </div>
                    <div class='hint' style='margin-top: 10px;'>Các dầm có cùng kích thước, thép dọc và thép đai sẽ được đặt cùng tên để giảm số lượng tiết diện.</div>
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

    function showTab(idx) {{
        document.querySelectorAll('.tab').forEach((t, i) => t.classList.toggle('active', i === idx));
        document.querySelectorAll('.tab-panel').forEach((p, i) => p.classList.toggle('active', i === idx));
    }}

    function loadForm() {{
        const setId = (id, val) => {{ const el = document.getElementById(id); if(el) el.value = val ?? ''; }};
        const setChk = (id, val) => {{ const el = document.getElementById(id); if(el) el.checked = val || false; }};
        
        // Tab 1
        setId('ZoneRatioStart', data.ZoneRatioStart);
        setId('ZoneRatioEnd', data.ZoneRatioEnd);
        setId('TorsionFactorTop', data.TorsionFactorTop);
        setId('TorsionFactorBot', data.TorsionFactorBot);
        setId('TorsionFactorSide', data.TorsionFactorSide);
        setId('CoverTop', data.CoverTop);
        setId('CoverBot', data.CoverBot);
        setId('CoverSide', data.CoverSide);
        setId('MinSpacing', data.MinSpacing);
        setId('WebBarMinHeight', data.WebBarMinHeight);
        setId('AutoLegsRules', data.AutoLegsRules);
        
        if(data.PreferredDiameters) setId('PreferredDiameters', data.PreferredDiameters.join(', '));
        if(data.StirrupDiameters) setId('StirrupDiameters', data.StirrupDiameters.join(', '));
        if(data.WebBarDiameters) setId('WebBarDiameters', data.WebBarDiameters.join(', '));
        
        setChk('AllowOddLegs', data.AllowOddLegs);
        setChk('AutoLegsFromWidth', data.AutoLegsFromWidth);
        
        // Tab 2
        setId('BeamPrefix', data.BeamPrefix);
        setId('GirderPrefix', data.GirderPrefix);
        setId('BeamSuffix', data.BeamSuffix);
        setChk('GroupByAxis', data.GroupByAxis);
        setChk('MergeSameSection', data.MergeSameSection);
        setChk('AutoRenameOnSectionChange', data.AutoRenameOnSectionChange);
    }}

    function doSave() {{
        const getVal = (id) => {{ 
            const el = document.getElementById(id); 
            if (!el) return '';
            const val = el.value;
            if (el.type === 'number') {{
                const num = parseFloat(val);
                return isNaN(num) ? 0 : num;
            }}
            return val || '';
        }};
        const getChk = (id) => {{ const el = document.getElementById(id); return el ? el.checked : false; }};
        const parseList = (str, fallback) => {{
            if (!str || !str.trim()) return fallback || [];
            const list = str.split(/[,\s]+/).map(s => parseInt(s.trim())).filter(n => !isNaN(n) && n > 0);
            return list.length > 0 ? list : (fallback || []);
        }};

        const result = {{
            ZoneRatioStart: getVal('ZoneRatioStart'),
            ZoneRatioEnd: getVal('ZoneRatioEnd'),
            TorsionFactorTop: getVal('TorsionFactorTop'),
            TorsionFactorBot: getVal('TorsionFactorBot'),
            TorsionFactorSide: getVal('TorsionFactorSide'),
            CoverTop: getVal('CoverTop'),
            CoverBot: getVal('CoverBot'),
            CoverSide: getVal('CoverSide'),
            MinSpacing: getVal('MinSpacing'),
            WebBarMinHeight: getVal('WebBarMinHeight'),
            AutoLegsRules: getVal('AutoLegsRules') || data.AutoLegsRules,
            
            PreferredDiameters: parseList(getVal('PreferredDiameters'), data.PreferredDiameters),
            StirrupDiameters: parseList(getVal('StirrupDiameters'), data.StirrupDiameters),
            WebBarDiameters: parseList(getVal('WebBarDiameters'), data.WebBarDiameters),
            
            AllowOddLegs: getChk('AllowOddLegs'),
            AutoLegsFromWidth: getChk('AutoLegsFromWidth'),
            
            BeamPrefix: getVal('BeamPrefix') || data.BeamPrefix,
            GirderPrefix: getVal('GirderPrefix') || data.GirderPrefix,
            BeamSuffix: getVal('BeamSuffix'),
            GroupByAxis: getChk('GroupByAxis'),
            MergeSameSection: getChk('MergeSameSection'),
            AutoRenameOnSectionChange: getChk('AutoRenameOnSectionChange'),
            
            // Preserve hidden values
            StirrupSpacings: data.StirrupSpacings,
            StirrupLegs: data.StirrupLegs,
            StirrupDiameter: data.StirrupDiameter,
            WebBarDiameter: data.WebBarDiameter,
            MaxBotRebar: data.MaxBotRebar
        }};

        window.chrome.webview.postMessage(JSON.stringify(result));
    }}

    function doCancel() {{ window.chrome.webview.postMessage('CANCEL'); }}
    
    loadForm();
</script>
</body>
</html>";
        }
    }
}
