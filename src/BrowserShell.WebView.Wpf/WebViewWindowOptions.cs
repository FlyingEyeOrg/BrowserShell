using WindowChromeKit.Wpf;

namespace BrowserShell.WebView.Wpf;

/// <summary>打开一个 Web 外壳窗口所需的全部设置。</summary>
public sealed class WebViewWindowOptions
{
    /// <summary>窗口首次导航到的绝对地址。为 null 时创建空白窗口。</summary>
    public Uri? Url { get; init; }

    /// <summary>窗口标题。为空时回退为地址主机名。</summary>
    public string? Title { get; init; }

    /// <summary>窗口宽度（设备无关像素）。默认 1024。</summary>
    public double Width { get; init; } = 1024;

    /// <summary>窗口高度（设备无关像素）。默认 768。</summary>
    public double Height { get; init; } = 768;

    /// <summary>窗口最小宽度（设备无关像素）。</summary>
    public double MinWidth { get; init; }

    /// <summary>窗口最小高度（设备无关像素）。</summary>
    public double MinHeight { get; init; }

    /// <summary>是否置于其他窗口之上。</summary>
    public bool Topmost { get; init; }

    /// <summary>是否在首次显示时居中于目标显示器。</summary>
    public bool Center { get; init; } = true;

    /// <summary>首次显示完成后是否激活窗口。</summary>
    public bool Focus { get; init; } = true;

    /// <summary>是否在任务栏显示。有 Owner 的窗口通常应设为 false。</summary>
    public bool ShowInTaskbar { get; init; } = true;

    /// <summary>是否允许用户调整大小。关闭后标题栏的最大化按钮也会禁用。</summary>
    public bool Resizable { get; init; } = true;

    /// <summary>标题栏几何骨架。见 <see cref="ChromeTitleBarStyle"/>。</summary>
    public ChromeTitleBarStyle TitleBarStyle { get; init; } = ChromeTitleBarStyle.Chrome;

    /// <summary>标题栏配色。见 <see cref="ChromeTitleBarPalette"/>。</summary>
    public ChromeTitleBarPalette TitleBarPalette { get; init; } = ChromeTitleBarPalette.Default;

    /// <summary>
    /// 允许导航到的 Origin 白名单（形如 <c>https://example.com</c>）。
    /// 为空表示不限制同源导航。
    /// </summary>
    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];

    /// <summary>关闭窗口前触发的裁决回调。返回 false 表示拒绝本次关闭。</summary>
    public Func<WebViewWindowClosingContext, CancellationToken, Task<bool>>? ClosingAsync { get; init; }

    /// <summary>窗口完成首次导航并显示后触发。</summary>
    public Func<WebViewWindow, Task>? OpenedAsync { get; init; }

    /// <summary>窗口关闭后触发一次。</summary>
    public Func<WebViewWindow, Task>? ClosedAsync { get; init; }

    /// <summary>
    /// 页面请求打开新窗口时触发。为 null 时在同一个窗口内导航到目标地址。
    /// </summary>
    public Func<WebViewWindow, Uri, Task>? NewWindowRequestedAsync { get; init; }
}

/// <summary>一次关闭裁决请求的上下文。</summary>
public sealed class WebViewWindowClosingContext
{
    internal WebViewWindowClosingContext(WebViewWindow window, string source)
    {
        Window = window;
        Source = source;
    }

    /// <summary>请求关闭的窗口。</summary>
    public WebViewWindow Window { get; }

    /// <summary>关闭来源：<c>WindowChrome</c> 表示标题栏按钮，<c>Page</c> 表示页面脚本。</summary>
    public string Source { get; }
}
