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
            // 层叠偏移：用 Left/Top 在【创建时】指定位置，窗口首次出现即在目标坐标，
            // 不会出现"先出现在别处、再跳到目标位置"的跳动。
            // 显示后再给 window.Left/Top 赋值做不到这一点——那时窗口已经在屏幕上了。
            var (left, top) = NextCascadePosition(1180, 780);

            await _shell.OpenAsync(new WebViewWindowOptions
            {
                Url = url,
                Title = url.Host,
                Width = 1180,
                Height = 780,
                MinWidth = 480,
                MinHeight = 320,
                Left = left,
                Top = top,
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

    /// <summary>
    /// 计算下一个层叠位置：每次打开都向右下偏移，并与主窗口（所有者）的左上角对齐。
    /// </summary>
    /// <param name="width">新窗口宽度，用于把窗口约束回工作区内。</param>
    /// <param name="height">新窗口高度，用于把窗口约束回工作区内。</param>
    /// <remarks>
    /// <para>示例刻意把坐标<b>算好再传入</b>，而不是打开后再移动窗口：后者会让用户看到跳动。</para>
    /// <para><c>Left</c>/<c>Top</c> 本身不做工作区裁剪（库只管按原值使用），
    /// 因此这里自行把坐标收进工作区，避免窗口跑到屏幕外。</para>
    /// </remarks>
    private (double Left, double Top) NextCascadePosition(double width, double height)
    {
        const double Step = 28;
        var workArea = SystemParameters.WorkArea;
        // 以主窗口为基准，四次一循环，避免无限偏出屏幕。
        var step = _opened % 4 * Step;
        var left = Math.Min(Left + step, workArea.Right - width);
        var top = Math.Min(Top + step, workArea.Bottom - height);
        return (Math.Max(workArea.Left, left), Math.Max(workArea.Top, top));
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
                // 本窗口图标：在创建时传入，避免"先出默认图标再跳出图标"的闪烁。
                // 故意用 Windows7 风格，与主窗口的 Windows10 不同，以便看出图标是每窗口独立的。
                Icon = WindowChromeIcons.Windows7,
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

    /// <summary>
    /// 打开 iframe 示例：左为正常 iframe，右为「响应永不结束」的悬挂 iframe。
    /// </summary>
    /// <remarks>
    /// <para>悬挂 iframe 会让宿主页的 <c>body.onload</c> 永不触发，从而复现 2.1.0 的
    /// 「窗口永久停在正在加载…」；2.1.1 起遮罩最迟 30 秒自动揭开。</para>
    /// </remarks>
    private async void OnOpenIframeClick(object sender, RoutedEventArgs e)
    {
        if (_shell is null)
        {
            StatusText.Text = "WebView2 环境尚未就绪。";
            return;
        }

        // 悬挂 iframe 需要一个"永不结束响应"的服务端：file:// 做不到，
        // 因此示例自带一个本地 HTTP 服务，把 /hang 的响应故意挂住不写完。
        HangServer.EnsureStarted();

        var page = Path.Combine(AppContext.BaseDirectory, "Assets", "iframe-demo.html");
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
                Title = "iframe 加载示例（含悬挂 iframe）",
                Width = 1180,
                Height = 720,
                MinWidth = 620,
                MinHeight = 420,
                BackgroundColor = System.Windows.Media.Color.FromRgb(0x1B, 0x1B, 0x1F),
            });

            _opened++;
            StatusText.Text = window.LastNavigationError is { } error
                ? $"已打开 iframe 示例，但首次导航有问题：{error}"
                : $"已打开 iframe 示例 {window.Id[..8]}（30 秒后遮罩自动揭开即兜底生效）。";
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
