using Autodesk.AutoCAD.ApplicationServices;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace DTS_Engine.UI.Forms
{
    /// <summary>
    /// DashboardControl - WebView2 control để hiển thị Dashboard.html
    /// </summary>
    public class DashboardControl : UserControl
    {
        private WebView2 _webView;

        public DashboardControl()
        {
            InitializeControl();
        }

        private async void InitializeControl()
        {
            _webView = new WebView2
            {
                Dock = DockStyle.Fill
            };
            this.Controls.Add(_webView);

            try
            {
                await _webView.EnsureCoreWebView2Async(null);

                // Load Dashboard.html từ embedded resources
                string html = LoadEmbeddedResource("DTS_Engine.UI.Resources.Dashboard.html");
                if (!string.IsNullOrEmpty(html))
                {
                    _webView.NavigateToString(html);
                }

                // Xử lý message từ JavaScript
                _webView.WebMessageReceived += WebView_WebMessageReceived;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebView2 init error: {ex.Message}");
            }
        }

        private void WebView_WebMessageReceived(object sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            string message = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(message)) return;

            // Thực thi lệnh AutoCAD
            try
            {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    doc.SendStringToExecute($"(command \"{message}\") ", false, false, false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Command error: {ex.Message}");
            }
        }

        private string LoadEmbeddedResource(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null) return null;
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
