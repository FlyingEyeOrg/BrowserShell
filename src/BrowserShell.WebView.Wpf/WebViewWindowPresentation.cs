using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;

namespace BrowserShell.WebView.Wpf;

/// <summary>
/// <see cref="WebViewWindow"/> 的 WPF 视觉元素构造：加载指示面板与初始化失败面板。
/// </summary>
/// <remarks>
/// <para>这些元素与窗口状态无关，仅是"长什么样"，因此从窗口类型中拆出，
/// 让 <see cref="WebViewWindow"/> 专注生命周期与时序。</para>
/// <para><b>空域说明</b>：本面板只在 WebView 尚未显示时可见。一旦 WebView 变为可见，
/// 其原生 HWND 无法被 WPF 元素覆盖，后续导航的加载指示改由注入页面的遮罩承担
/// （见 <see cref="WebViewPresentationMask"/>）。</para>
/// </remarks>
internal static class WebViewWindowPresentation
{
    /// <summary>
    /// 创建"正在加载…"面板，作为首次加载期间（WebView 尚不可见时）的内容区。
    /// </summary>
    /// <param name="background">
    /// 面板底色，应与 <see cref="WebViewWindowOptions.BackgroundColor"/> 一致，
    /// 否则深色页面在首次加载期间会露出浅色底（即"白闪"）。
    /// </param>
    /// <param name="foreground">指示文字颜色，需与 <paramref name="background"/> 形成足够对比。</param>
    public static Border CreateLoadingSurface(Brush background, Brush? foreground = null)
    {
        ArgumentNullException.ThrowIfNull(background);
        var progress = new ProgressBar
        {
            Width = 180,
            Height = 3,
            IsIndeterminate = true,
            Margin = new Thickness(0, 0, 0, 12),
        };
        var label = new TextBlock
        {
            Text = "正在加载…",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            Foreground = foreground ?? Brushes.DimGray,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(progress);
        content.Children.Add(label);
        return new Border { Background = background, Child = content };
    }

    /// <summary>
    /// 把初始化失败的原因显示在内容区，替代加载指示。
    /// </summary>
    /// <param name="surface">承载面板（加载与失败共用同一元素，切换其 <c>Child</c>）。</param>
    /// <param name="webView">需要同时隐藏的 WebView 控件。</param>
    /// <param name="exception">失败原因。</param>
    public static void ShowFailure(Border surface, WebView2 webView, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(webView);
        ArgumentNullException.ThrowIfNull(exception);

        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 520,
            Margin = new Thickness(24),
        };
        content.Children.Add(new TextBlock
        {
            Text = "无法初始化 WebView2",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Firebrick,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        content.Children.Add(new TextBlock
        {
            Text = exception.Message,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Foreground = Brushes.DimGray,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        content.Children.Add(new TextBlock
        {
            Text = "请确认已安装 WebView2 Runtime。",
            Margin = new Thickness(0, 12, 0, 0),
            Foreground = Brushes.Gray,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        surface.Child = content;
        surface.Visibility = Visibility.Visible;
        webView.Visibility = Visibility.Collapsed;
    }
}
