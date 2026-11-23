using System.Windows;
using System.Windows.Media;
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

    /// <summary>是否在首次显示时居中于目标显示器。指定了 <see cref="Left"/> 与 <see cref="Top"/> 时被忽略。</summary>
    public bool Center { get; init; } = true;

    /// <summary>
    /// 首次显示时的屏幕横坐标（设备无关像素，虚拟桌面坐标）。与 <see cref="Top"/> 一起指定。
    /// </summary>
    /// <remarks>
    /// <para><b>优先级</b>：显式 <see cref="Left"/> + <see cref="Top"/> &gt; <see cref="Center"/> &gt; 系统默认位置。</para>
    ///
    /// <para><b>必须成对</b>：只指定其中一个时视为未指定，回退为 <see cref="Center"/> 的逻辑
    /// （单个坐标不成位置）。<c>NaN</c> 与无穷大同理，按未指定处理。</para>
    ///
    /// <para><b>不做工作区裁剪</b>：坐标按原值使用，不会被移动或缩放到显示器工作区内，
    /// 也不会被修正为可见区域。合法性由调用方负责——传入屏幕外的坐标，
    /// 窗口就在屏幕外（多显示器布局中这是有意义的用法）。</para>
    ///
    /// <para><b>在显示之前生效</b>：该坐标在窗口创建/显示之前应用，因此窗口<b>首次出现即在目标坐标</b>，
    /// 不会出现"先出现在别处、再跳到目标位置"。这也是它必须在创建时传入、
    /// 而不能在 <see cref="WebViewShell.OpenAsync"/> 返回后再设 <c>window.Left</c> 的原因：
    /// 后者返回时窗口已在屏幕上，改动必然被用户看见。</para>
    ///
    /// <para><b>只在首次显示时生效</b>：<see cref="WebViewWindow.ApplyOptions"/> 不会重新应用位置，
    /// 以免在窗口可见时产生跳动。</para>
    ///
    /// <para>为 <c>null</c>（默认）时按 <see cref="Center"/> 决定，行为与未提供本属性完全一致。</para>
    /// </remarks>
    public double? Left { get; init; }

    /// <summary>
    /// 首次显示时的屏幕纵坐标（设备无关像素，虚拟桌面坐标）。与 <see cref="Left"/> 一起指定。
    /// </summary>
    /// <remarks>取值语义、优先级与"必须成对"的约束同 <see cref="Left"/>。</remarks>
    public double? Top { get; init; }

    /// <summary>首次显示完成后是否激活窗口。</summary>
    /// <remarks>
    /// <para>仅决定窗口是否成为<b>活动窗口</b>；键盘焦点是否落到页面内容由
    /// <see cref="FocusPage"/> 控制。</para>
    /// </remarks>
    public bool Focus { get; init; } = true;

    /// <summary>
    /// 激活窗口后，是否把键盘焦点交给页面内容。默认 <c>true</c>。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要这个开关</b>：WebView2 承载在原生 HWND 上，
    /// <see cref="Window.Activate"/> 只激活窗口，焦点会停在窗口本身——
    /// 用户看到窗口在前台，但直接在页面里打字没有反应，必须先点一下页面。
    /// 本库因此在激活后把焦点显式移交给页面。</para>
    ///
    /// <para><b>何时设为 <c>false</c></b>：纯展示页（不需要键盘输入），或宿主希望在
    /// <see cref="OpenedAsync"/> 里先操作自己的 WPF 元素、不希望被页面抢走焦点。</para>
    ///
    /// <para>仅在 <see cref="Focus"/> 为 <c>true</c> 时生效。</para>
    /// </remarks>
    public bool FocusPage { get; init; } = true;

    /// <summary>
    /// <see cref="WebViewShell.OpenAsync"/> 是否等待首次导航<b>完全结束</b>（<c>body.onload</c>）后才返回。
    /// 默认 <c>false</c>：窗口显示后立即返回，导航在后台继续。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么默认不等待</b>：WebView2 的 <c>NavigationCompleted</c> 以 <c>body.onload</c>
    /// 为前提，要等<b>所有</b>子资源（iframe、图片、脚本）结束。页面里只要有一个慢资源或悬挂资源，
    /// 该事件就迟迟不来——<c>OpenAsync</c> 会被拖住数秒至 30 秒超时。实测：子资源延迟 6 秒
    /// ⇒ <c>OpenAsync</c> 等 6.99 秒；含悬挂 iframe ⇒ 等满 30 秒。这类延迟会被误认为
    /// "WebView2 环境初始化慢"，而环境初始化实测仅约 15 毫秒。</para>
    ///
    /// <para><b>不等待时的可见性</b>：窗口自 <c>Show()</c> 起就已可见；页面遮罩在
    /// <c>DOMContentLoaded</c>（文档解析完成）时揭开，因此用户仍能正常看到页面。
    /// 导航结果仍可从 <see cref="WebViewWindow.LastNavigationError"/> 观察（后台最长等 30 秒）。</para>
    ///
    /// <para><b>何时该设为 <c>true</c></b>：调用方确实需要"导航已结束"这一语义时才设
    /// （例如紧接着要抓取完整 DOM），并需自行承担慢资源带来的等待。</para>
    ///
    /// <para>该属性只影响首次导航，不影响 <see cref="WebViewWindow.NavigateAsync"/> 等后续导航
    /// （后者始终立即返回）。</para>
    /// </remarks>
    public bool WaitForFirstScreen { get; init; }

    /// <summary>是否在任务栏显示。有 Owner 的窗口通常应设为 false。</summary>
    public bool ShowInTaskbar { get; init; } = true;

    /// <summary>是否允许用户调整大小。关闭后标题栏的最大化按钮也会禁用。</summary>
    public bool Resizable { get; init; } = true;

    /// <summary>标题栏几何骨架。默认为 <see cref="ChromeTitleBarStyle.Chrome"/>。</summary>
    public ChromeTitleBarStyle TitleBarStyle { get; init; } = ChromeTitleBarStyle.Chrome;

    /// <summary>标题栏配色。默认为 <see cref="ChromeTitleBarPalette.Default"/>，即该样式自带的那套。</summary>
    public ChromeTitleBarPalette TitleBarPalette { get; init; } = ChromeTitleBarPalette.Default;

    /// <summary>
    /// <b>本窗口</b>的图标（标题栏左上角、任务栏、Alt+Tab 与窗口切换器使用同一个）。
    /// 为 <c>null</c> 时回退为 exe 内嵌图标，再回退为系统默认图标。
    /// </summary>
    /// <remarks>
    /// <para><b>作用范围仅限本窗口</b>：它不修改 exe 内嵌图标，也不影响同一进程中其他窗口。
    /// 每个窗口可有各自的图标。exe 图标由项目的 <c>&lt;ApplicationIcon&gt;</c> 决定，
    /// 与本属性无关。</para>
    ///
    /// <para><b>取值优先级</b>（WPF 定义，已核实其源码注释）：</para>
    /// <list type="number">
    ///   <item><description>本属性提供的 <see cref="ImageSource"/>；</description></item>
    ///   <item><description>否则用 exe 内嵌图标（此时读取本属性返回 <c>null</c>）；</description></item>
    ///   <item><description>否则交给系统默认图标。</description></item>
    /// </list>
    ///
    /// <para><b>在创建时传入的好处</b>：本属性经 <see cref="WebViewWindowOptions"/> 在窗口显示前生效，
    /// 因此不会出现"窗口先以无图标/默认图标出现、随后图标再跳出"的闪烁。
    /// 若在窗口创建后再给 <c>WebViewWindow.Icon</c> 赋值，则会有这一帧跳变。</para>
    ///
    /// <para><b>建议做法</b>：用 <c>BitmapImage</c> 加载并设置 <c>BitmapCacheOption.OnLoad</c>，
    /// 否则该对象会持有文件句柄，导致图标文件在窗口存活期间无法删除或替换：</para>
    /// <code>
    /// var icon = new BitmapImage();
    /// icon.BeginInit();
    /// icon.CacheOption = BitmapCacheOption.OnLoad;   // 关键：立即读入，不锁定文件
    /// icon.UriSource = new Uri("pack://application:,,,/Assets/app.ico");
    /// icon.EndInit();
    /// icon.Freeze();                                 // 跨线程共享与性能更佳
    /// </code>
    /// <para>本类型<b>不负责释放</b>该对象（与 WPF 一致：<c>Window</c> 关闭时不释放 <c>Icon</c>）。</para>
    /// </remarks>
    public ImageSource? Icon { get; init; }

    /// <summary>
    /// 未加载出内容期间的窗口底色，默认白色。承载深色页面时应设为与页面一致的深色，以消除白闪。
    /// </summary>
    /// <remarks>
    /// <para>该颜色同时作用于三处：</para>
    /// <list type="bullet">
    ///   <item><description>WebView 的 <c>DefaultBackgroundColor</c>——WebView 在无内容时
    ///   （首次导航前、两次导航之间）显示的底色，也衬在未定义背景的页面之下；</description></item>
    ///   <item><description>窗口内容根（<c>Grid</c>）的背景——首次加载期间可见的就是它；</description></item>
    ///   <item><description>注入遮罩的背景——首次加载之后各次导航的加载指示。</description></item>
    /// </list>
    ///
    /// <para><b>限制一（官方记载）</b>：WebView2 文档指出「仅通过属性设置该颜色，仍可能在
    /// 属性生效前出现一次白闪」，并称改用环境变量 <c>WEBVIEW2_DEFAULT_BACKGROUND_COLOR</c>
    /// 可解决。本库<b>未</b>采用环境变量，因为它进程级生效、只能设置一次，
    /// 无法支持「不同窗口不同底色」。因此极早期的白闪在深色场景下仍可能有一帧残留。</para>
    ///
    /// <para><b>限制二</b>：WebView2 仅支持<b>不透明</b>或<b>全透明</b>（alpha 为 0 或 255）；
    /// 半透明会失败。此处不做校验，传入半透明值将由 WebView2 拒绝。</para>
    /// </remarks>
    public Color BackgroundColor { get; init; } = Colors.White;

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
