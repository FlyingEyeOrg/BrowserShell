using System.IO;
using System.Windows;
using WindowChromeKit.Wpf;

namespace BrowserShell.WebView.Wpf.Sample;

/// <summary>示例主窗口：用窗口与控件演示 <see cref="WebViewShell"/> 的全部公开能力。</summary>
public partial class MainWindow : ChromeWindow
{
    private static readonly ChromeTitleBarStyle[] Styles =
    [
        ChromeTitleBarStyle.Chrome,
        ChromeTitleBarStyle.Windows,
        ChromeTitleBarStyle.VsCode,
    ];

    private static readonly ChromeTitleBarPalette[] Palettes =
    [
        ChromeTitleBarPalette.Default,
        ChromeTitleBarPalette.ElementPlusPrimary,
        ChromeTitleBarPalette.ElementPlusDark,
        ChromeTitleBarPalette.ElementPlusNeutral,
    ];

    private static readonly string[] ClosingModes =
    [
        "直接关闭",
        "允许（经裁决回调）",
        "拒绝",
    ];

    private WebViewShell? _shell;
    private int _opened;

    public MainWindow()
    {
        InitializeComponent();
        StyleBox.ItemsSource = Styles;
        // 默认跟随 WebViewWindowOptions 的默认值（Chrome 骨架 + 自带配色）。
        StyleBox.SelectedItem = ChromeTitleBarStyle.Chrome;
        PaletteBox.ItemsSource = Palettes;
        PaletteBox.SelectedItem = ChromeTitleBarPalette.Default;
        ClosingBox.ItemsSource = ClosingModes;
        ClosingBox.SelectedIndex = 1;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // 整个进程只创建一个 WebView2 环境；所有窗口共享它，但各自使用隔离 Profile。
            _shell = await WebViewShell.CreateAsync(Dispatcher);
            StatusText.Text = "WebView2 环境已就绪。";
        }
        catch (Exception exception)
        {
            // 最常见的原因是未安装 WebView2 Runtime，或运行在非 Windows 平台上。
            StatusText.Text = $"初始化失败：{exception.Message}";
        }
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        if (_shell is not null)
        {
            await _shell.DisposeAsync();
        }
    }

    private async void OnOpenClick(object sender, RoutedEventArgs e)
    {
        if (_shell is null)
        {
            StatusText.Text = "WebView2 环境尚未就绪。";
            return;
        }

        if (!Uri.TryCreate(UrlBox.Text, UriKind.Absolute, out var url))
        {
            StatusText.Text = "地址无效，需为绝对地址（含 http/https）。";
            return;
        }

        var style = StyleBox.SelectedItem is ChromeTitleBarStyle selectedStyle
            ? selectedStyle
            : ChromeTitleBarStyle.Chrome;
        var palette = PaletteBox.SelectedItem is ChromeTitleBarPalette selectedPalette
            ? selectedPalette
            : ChromeTitleBarPalette.Default;
        var mode = ClosingBox.SelectedIndex;

        try
        {
            await _shell.OpenAsync(new WebViewWindowOptions
            {
                Url = url,
                Title = url.Host,
                Width = 1180,
                Height = 780,
                MinWidth = 480,
                MinHeight = 320,
                Topmost = TopmostBox.IsChecked == true,
                Resizable = ResizableBox.IsChecked == true,
                TitleBarStyle = style,
                TitleBarPalette = palette,
                // 勾选后只允许 http(s)://host 内的导航；跨 Origin 导航与子窗口都会被拦截。
                AllowedOrigins = OriginsBox.IsChecked == true ? [$"{url.Scheme}://{url.Authority}"] : [],
                // 模式 0 不提供回调，关闭走默认路径；模式 1/2 由回调裁决。
                ClosingAsync = mode == 0 ? null : (_, _) => Task.FromResult(mode != 2),
                // 首次导航完成并显示后触发；此处回报窗口标识与当前窗口数。
                OpenedAsync = opened =>
                {
                    StatusText.Text = opened.LastNavigationError is { } error
                        ? $"已打开 {opened.Id[..8]}，但首次导航有问题：{error}"
                        : $"已打开 {opened.Id[..8]}（{_shell?.WindowCount ?? 0} 个窗口）。";
                    return Task.CompletedTask;
                },
                ClosedAsync = closed =>
                {
                    StatusText.Text = $"已关闭 {closed.Id[..8]}（{_shell?.WindowCount ?? 0} 个窗口）。";
                    return Task.CompletedTask;
                },
            });

            _opened++;
        }
        catch (Exception exception)
        {
            StatusText.Text = $"打开失败：{exception.Message}";
        }
    }

    private async void OnOpenLocalClick(object sender, RoutedEventArgs e)
    {
        if (_shell is null)
        {
            StatusText.Text = "WebView2 环境尚未就绪。";
            return;
        }

        // 本地示例页演示 browserShell.window.* 桥：最小化/最大化/还原/请求关闭。
        var page = Path.Combine(AppContext.BaseDirectory, "Assets", "demo.html");
        if (!File.Exists(page))
        {
            StatusText.Text = $"找不到本地示例页：{page}";
            return;
        }

        try
        {
            var window = await _shell.OpenAsync(new WebViewWindowOptions
            {
                Url = new Uri(page),
                Title = "BrowserShell 本地示例页",
                Width = 760,
                Height = 620,
                MinWidth = 420,
                MinHeight = 300,
                // 与 demo.html 的深色底一致：底色贯通 WebView 默认底色、窗口内容根与加载
                // 遮罩三处，使加载期间不出现白色闪烁（深色页面配白色底板即为白闪）。
                BackgroundColor = System.Windows.Media.Color.FromRgb(0x1B, 0x1B, 0x1F),
                // 不指定标题栏样式，使用 WebViewWindowOptions 的默认值（Chrome）。
            });

            _opened++;
            StatusText.Text = window.LastNavigationError is { } error
                ? $"已打开本地示例页，但首次导航有问题：{error}"
                : $"已打开本地示例页 {window.Id[..8]}。";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"打开失败：{exception.Message}";
        }
    }

    private async void OnCloseAllClick(object sender, RoutedEventArgs e)
    {
        if (_shell is null)
        {
            return;
        }

        foreach (var window in _shell.Windows.ToArray())
        {
            await window.CloseAsync("Sample");
        }

        StatusText.Text = $"已请求关闭全部窗口（本次共打开 {_opened} 个）。";
        _opened = 0;
    }
}
