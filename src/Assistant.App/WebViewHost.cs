using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Windows.Forms;

namespace Assistant.App;

/// <summary>
/// WebView2 主視窗宿主：
/// - 建立 WinForms 視窗並嵌入 WebView2 控制項
/// - 向 WebView2 註冊自訂 URI Scheme <c>app://</c>
/// - 導向至 <c>app://frontend/index.html</c>（Embedded Resource）
/// - 委派訊息收發給 <see cref="IpcBridge"/>
/// </summary>
internal sealed class WebViewHost : IDisposable
{
    private readonly IpcBridge _ipcBridge;
    private readonly ResourceLoader _resourceLoader;
    private Form? _form;
    private WebView2? _webView;

    public WebViewHost(IpcBridge ipcBridge, ResourceLoader resourceLoader)
    {
        _ipcBridge = ipcBridge;
        _resourceLoader = resourceLoader;
    }

    /// <summary>阻塞式啟動：建立視窗並進入訊息迴圈</summary>
    public void Run()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        _form = new Form
        {
            Text = "個人知識庫助理",
            Width = 1280,
            Height = 800,
            StartPosition = FormStartPosition.CenterScreen,
        };

        _webView = new WebView2 { Dock = DockStyle.Fill };
        _form.Controls.Add(_webView);
        _form.Load += OnFormLoadAsync;

        Application.Run(_form);
    }

    private async void OnFormLoadAsync(object? sender, EventArgs e)
    {
        await InitializeWebViewAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        if (_webView is null) return;

        // 建立 WebView2 環境（使用預設 Edge WebView2 Runtime）
        var env = await CoreWebView2Environment.CreateAsync();
        await _webView.EnsureCoreWebView2Async(env);

        var core = _webView.CoreWebView2;

        // ── 1. 註冊自訂 URI Scheme 過濾器 ──
        core.AddWebResourceRequestedFilter("app://*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;

        // ── 2. 設定 IPC：接收前端 postMessage ──
        core.WebMessageReceived += OnWebMessageReceived;

        // ── 3. 導向前端入口 ──
#if DEBUG
        // 開發模式：直接指向 Vite Dev Server，享受 HMR
        core.Navigate("http://localhost:5173");
#else
        // 生產模式：載入嵌入的靜態資產
        core.Navigate("app://frontend/index.html");
#endif
    }

    /// <summary>攔截 <c>app://*</c> 請求，從 Embedded Resources 回應靜態資產</summary>
    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        if (_webView?.CoreWebView2 is null) return;

        // "app://frontend/assets/index.js" → "frontend/assets/index.js"
        var uri = new Uri(args.Request.Uri);
        var resourcePath = uri.Host + uri.AbsolutePath.TrimStart('/');

        var (stream, contentType) = _resourceLoader.Load(resourcePath);
        if (stream is not null)
        {
            args.Response = _webView.CoreWebView2.Environment
                .CreateWebResourceResponse(stream, 200, "OK", $"Content-Type: {contentType}");
        }
    }

    /// <summary>接收前端 postMessage，委派給 IpcBridge 處理</summary>
    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        var json = args.TryGetWebMessageAsString();
        if (json is null) return;

        var responseJson = await _ipcBridge.HandleAsync(json);
        _webView?.CoreWebView2.PostWebMessageAsString(responseJson);
    }

    public void Dispose()
    {
        _webView?.Dispose();
        _form?.Dispose();
    }
}
