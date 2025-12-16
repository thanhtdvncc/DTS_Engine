using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using DTS_Engine.Core.Data;

namespace DTS_Engine.UI.Forms
{
    /// <summary>
    /// Cửa sổ cấu hình thông số cốt thép (Modern UI - 2 Tabs)
    /// HTML được load từ Embedded Resource để tách biệt View khỏi Logic (MVC)
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
            this.Size = new Size(620, 620);
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

                // Load HTML từ Embedded Resource
                string html = LoadHtmlFromResource();
                _webView.NavigateToString(html);
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                if (!this.IsDisposed)
                    MessageBox.Show("Lỗi khởi tạo: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// Đọc file HTML từ Embedded Resource và thay thế placeholder bằng JSON settings
        /// </summary>
        private string LoadHtmlFromResource()
        {
            // Resource name format: [Namespace].[Folder].[FileName]
            string resourceName = "DTS_Engine.UI.Resources.RebarConfig.html";

            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    // Fallback: list tất cả resource names để debug
                    var names = assembly.GetManifestResourceNames();
                    throw new Exception($"Không tìm thấy resource '{resourceName}'. Available: {string.Join(", ", names)}");
                }

                using (StreamReader reader = new StreamReader(stream))
                {
                    string html = reader.ReadToEnd();

                    // Thay thế placeholder bằng JSON settings thực
                    string settingsJson = JsonConvert.SerializeObject(_settings);
                    html = html.Replace("__SETTINGS_JSON__", settingsJson);

                    return html;
                }
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

                // Deserialize với ObjectCreationHandling.Replace để thay thế Lists hoàn toàn
                var serSettings = new JsonSerializerSettings
                {
                    ObjectCreationHandling = ObjectCreationHandling.Replace
                };
                JsonConvert.PopulateObject(json, _settings, serSettings);

                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi lưu cấu hình: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
