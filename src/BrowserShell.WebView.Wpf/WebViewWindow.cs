using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WindowChromeKit.Wpf;

namespace BrowserShell.WebView.Wpf;

/// <summary>
/// Web 外壳窗口：由 <see cref="ChromeWindow"/> 提供原生窗口边框与标题栏、由 WebView2 承载页面。
/// </summary>
/// <remarks>
/// <para><b>职责</b>：只负责内容承载、导航、加载呈现、关闭裁决与页面侧控制桥，
/// 不包含任何业务协议（登录、权限、业务弹窗均由宿主或页面自身实现）。
/// 原生边框、拖动、缩放、Snap Layout、DWM 阴影、DPI 与工作区约束全部由
/// <see cref="ChromeWindow"/> 提供，本类型不重复实现。</para>
///
/// <para><b>创建方式</b>：构造函数为 <c>internal</c>，实例只能经
/// <see cref="WebViewShell.OpenAsync"/> 创建。这样可保证：</para>
/// <list type="bullet">
///   <item><description>同一进程内共享唯一的 WebView2 环境；</description></item>
///   <item><description><see cref="Id"/> 在管理器注册表内唯一（并因此获得独立的 Profile 隔离）；</description></item>
///   <item><description>WebView2 Controller 的并发初始化被串行化。</description></item>
/// </list>
///
/// <para><b>线程模型</b>：默认要求在 UI 线程（创建窗口的 <c>Dispatcher</c>）上调用。触碰窗口或
/// WebView2 的调用会经 <c>DispatcherObject.VerifyAccess()</c> 校验线程，
/// 在错误的线程上抛 <see cref="InvalidOperationException"/>。</para>
/// <para>为兑现"可从任意线程调用"的约定，下列成员会在内部自动封送回 UI 线程，
/// 已在 UI 线程时则直接同步执行：<see cref="InitializeAndShowAsync"/>、
/// <see cref="CloseAsync"/>、<see cref="ClosePermanently"/> 与 <see cref="DisposeAsync"/>。
/// 其余成员（导航、窗口命令、属性读取）必须在 UI 线程调用。</para>
///
/// <para><b>加载时序</b>：窗口先显示、内容后加载——这样 WebView2 初始化失败时窗口仍然保留，
/// 并以内容区面板呈现失败原因，而不是让窗口"一闪即消"。
/// 加载指示分两个阶段：首次加载期间由 WPF 层面板承担（此时 WebView 尚不可见）；
/// 首次加载之后的每次导航由注入页面 DOM 的遮罩承担（此时 WPF 层无法覆盖原生 HWND，即空域限制）。</para>
///
/// <para><b>关闭语义</b>：关闭有三个来源——标题栏按钮、页面脚本的
/// <c>browserShell.window.close()</c>、宿主的 <see cref="CloseAsync"/>。
/// 三者统一经 <see cref="WebViewWindowOptions.ClosingAsync"/> 裁决；未提供该回调时直接关闭。
/// 窗口真正关闭后，会先由所有者将其移出注册表，再回调
/// <see cref="WebViewWindowOptions.ClosedAsync"/>。</para>
///
/// <para><b>安全默认值</b>：宿主对象访问、脚本对话框、DevTools、默认右键菜单、状态栏
/// 一律禁用；权限请求一律拒绝、下载一律取消。即"默认拒绝，由宿主按需放开"。</para>
/// </remarks>
/// <seealso cref="WebViewShell"/>
/// <seealso cref="WebViewWindowOptions"/>
public sealed class WebViewWindow : ChromeWindow, IAsyncDisposable
{
    private readonly WebView2 _webView = new()
    {
        DefaultBackgroundColor = System.Drawing.Color.White,
        MinWidth = 0,
        MinHeight = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        Visibility = Visibility.Hidden,
    };
    // 背景仅作字段默认值，实际取值在构造时由 ApplyOptions 按 BackgroundColor 覆盖。
    private readonly Grid _presentationRoot = new() { Background = Brushes.White };
    private readonly Border _initializationSurface = WebViewWindowPresentation.CreateLoadingSurface(Brushes.White);
    private readonly CoreWebView2Environment _environment;
    private readonly CoreWebView2ControllerOptions _controllerOptions;
    private readonly WebView2InitializationCoordinator _webViewInitialization;
    private readonly Func<WebViewWindowClosingContext, CancellationToken, Task<bool>>? _closingAsync;
    private readonly Func<WebViewWindow, Task>? _openedAsync;
    private readonly Func<WebViewWindow, Task>? _closedAsync;
    private readonly Func<WebViewWindow, Uri, Task>? _newWindowAsync;
    private readonly Func<WebViewWindow, Task>? _onClosed;

    private WebViewWindowOptions _options;
    private HashSet<string> _allowedOrigins;
    private bool _forceClose;
    private bool _closePending;
    // 0 = 未关闭，1 = 关闭已确立。用 int 以便跨线程读取（IsClosed / 封送守卫均为任意线程可见）。
    private int _closedOnceFlag;
    // 关闭通知的 Task，保证「所有者注销 + 宿主回调」只执行一次；
    // 后到的调用者复用同一个 Task，从而不会在回调完成前提前返回。
    private Task? _closedNotification;
    // 正在执行关闭回调的线程 ID（0 表示无）。用于识别 DisposeAsync 的重入：
    // 宿主在 ClosedAsync 内调 DisposeAsync 时不得等待该回调自身的通知，否则自锁。
    private int _closedCallbackThreadId;
    private int _webViewDisposed;
    private CancellationTokenSource? _initializationCancellation;
    private long _navigationGeneration;

    internal WebViewWindow(
        string id,
        WebViewWindowOptions options,
        CoreWebView2Environment environment,
        CoreWebView2ControllerOptions controllerOptions,
        WebView2InitializationCoordinator webViewInitialization,
        Func<WebViewWindow, Task>? onClosed = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(controllerOptions);
        ArgumentNullException.ThrowIfNull(webViewInitialization);

        Id = id;
        _options = options;
        _environment = environment;
        _controllerOptions = controllerOptions;
        _webViewInitialization = webViewInitialization;
        _onClosed = onClosed;
        _closingAsync = options.ClosingAsync;
        _openedAsync = options.OpenedAsync;
        _closedAsync = options.ClosedAsync;
        _newWindowAsync = options.NewWindowRequestedAsync;
        _allowedOrigins = NormalizeOrigins(options.AllowedOrigins);
        // 原生边框、拖动、Snap、DWM 阴影与工作区约束由 WindowChromeKit 提供。
        DisplayConfigurationChanged += OnDisplayConfigurationChanged;
        ApplyOptions(options);
        Content = _presentationRoot;
        _presentationRoot.Children.Add(_webView);
        _presentationRoot.Children.Add(_initializationSurface);
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            DisplayConfigurationChanged -= OnDisplayConfigurationChanged;
            DisposeWebViewOnce();
            _ = NotifyClosedOnceAsync();
        };
    }

    /// <summary>
    /// 窗口标识。<b>在同一 <see cref="WebViewShell"/> 的注册表内唯一</b>，由
    /// <see cref="WebViewShell.OpenAsync"/> 指定或自动生成（GUID 的 <c>N</c> 格式）。
    /// </summary>
    /// <remarks>
    /// <para>该标识同时承担三项职责，因此它的唯一性不是记账问题，而是功能前提：</para>
    /// <list type="number">
    ///   <item><description><b>注册表键</b>：<see cref="WebViewShell.TryGetWindow"/>、
    ///   <see cref="WebViewShell.CloseAsync"/> 与关闭时的注销都依赖它定位窗口。</description></item>
    ///   <item><description><b>Profile 隔离种子</b>：它经 SHA-256 派生出 WebView2 的
    ///   <c>ProfileName</c>。因此<b>相同标识会导致两个窗口共用 Cookie 与缓存</b>，隔离失效——
    ///   这是唯一性必须被强制的根本原因。</description></item>
    ///   <item><description><b>页面可见标识</b>：注入页面的脚本会把它暴露为
    ///   <c>browserShell.window.windowId</c>，供同一页面被多个窗口承载时区分自身。</description></item>
    /// </list>
    /// <para>唯一性在 <see cref="WebViewShell.OpenAsync"/> 的 UI 线程回调内以
    /// 查重 + <c>Add</c> 的原子方式强制；重复即时抛 <see cref="InvalidOperationException"/>。
    /// 窗口真正关闭后该标识被释放，可再次使用。</para>
    /// </remarks>
    public string Id { get; }

    /// <summary>
    /// 窗口是否已关闭（关闭流程已走完，<see cref="WebViewWindowOptions.ClosedAsync"/> 已触发或即将触发）。
    /// </summary>
    /// <remarks>该值在一次生命周期内单调递增一次，关闭后不会回到 <c>false</c>。</remarks>
    public bool IsClosed => Volatile.Read(ref _closedOnceFlag) != 0;

    /// <summary>
    /// 最近一次<b>首次导航</b>相关的失败原因；为 <c>null</c> 表示无已知失败。
    /// </summary>
    /// <remarks>
    /// <para>下列情形会写入该值：</para>
    /// <list type="bullet">
    ///   <item><description>WebView2 环境/Controller 初始化失败（格式为 <c>异常类型: 消息</c>）；</description></item>
    ///   <item><description>首次导航失败（网络不可达、DNS 失败等），值为"首次导航失败。"；</description></item>
    ///   <item><description>首次导航超时（30 秒），值为"首次导航超时。"。</description></item>
    /// </list>
    /// <para><b>重置时机</b>：任何一次<b>成功</b>的导航都会把它清回 <c>null</c>。
    /// 因此宿主可据此判断"当前是否正常"：即使首次加载失败，用户在页面内重试成功
    /// （或宿主调用 <see cref="ReloadAsync"/>）后该属性会自动恢复为 <c>null</c>。</para>
    /// <para><b>失败时为何不写入</b>：官方文档指出 <c>IsSuccess</c> 为 <c>false</c> 也可能是
    /// 非灾难性情形（页面主动跳转、<c>window.stop()</c>、被 <see cref="WebViewWindowOptions.AllowedOrigins"/>
    /// 主动拦截、应用自行取消），一律记为失败会产生大量误导信息，因此不做记录。</para>
    /// <para>首次导航失败不会抛异常：窗口本身已可用，调用方可自行重试。</para>
    /// </remarks>
    public string? LastNavigationError { get; private set; }

    /// <summary>
    /// 创建 HWND、完成首次导航并显示窗口。由 <see cref="WebViewShell.OpenAsync"/> 调用。
    /// </summary>
    /// <param name="token">取消令牌，用于取消首次导航的等待。</param>
    /// <remarks>
    /// <para><b>顺序约束</b>：必须先 <c>EnsureHandle()</c> 创建原生 HWND 并让 DWM 接管客户区，
    /// 再创建 WebView2 Controller。顺序颠倒会让自绘标题栏落在 DWM 尚未接管的时间点，出现绘制错位。</para>
    ///
    /// <para><b>失败隔离</b>：本方法<b>不因 WebView2 初始化失败而抛出</b>。窗口已先显示，
    /// 失败时改为在内容区呈现失败面板（含原因与"请确认已安装 WebView2 Runtime"提示），
    /// 避免窗口一闪即消。失败原因同时记入 <see cref="LastNavigationError"/>。</para>
    ///
    /// <para><b>取消语义</b>：若初始化过程中已确认关闭（<c>_forceClose</c>），
    /// 由此产生的 <see cref="OperationCanceledException"/> 被视为正常取消而非失败。</para>
    ///
    /// <para>首次导航成功后触发 <see cref="WebViewWindowOptions.OpenedAsync"/>；
    /// 若窗口在此期间已关闭，则不再触发。</para>
    /// </remarks>
    public Task InitializeAndShowAsync(CancellationToken token = default) =>
        RunOnUiThreadAsync(() => InitializeAndShowCoreAsync(token));

    private async Task InitializeAndShowCoreAsync(CancellationToken token)
    {
        _initializationCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        token = _initializationCancellation.Token;
        var cancelled = false;
        try
        {
            // 先创建 HWND 并挂接原生边框，再创建 controller；顺序错误会让自绘标题栏
            // 落在 DWM 尚未接管客户区的时候。
            _ = new WindowInteropHelper(this).EnsureHandle();
            Show();
            if (_options.Center) WindowPlacementService.CenterOnTargetMonitor(this);
            else WindowPlacementService.ConstrainToWorkArea(this);
            UpdateLayout();
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Loaded, token);
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render, token);

            // WebView2 初始化与首次导航单独兜底：窗口已经显示出来了，WebView 失败时
            // 保留窗口并把错误呈现给用户，而不是让窗口一闪即消。
            await InitializeWebViewAsync(token);
        }
        catch (OperationCanceledException) when (_forceClose)
        {
            // 加载过程中已确认关闭，不把本地主动取消当作初始化失败。
            cancelled = true;
        }
        catch (Exception exception)
        {
            // WebView2 Runtime 缺失、Profile 创建失败等：保留窗口并显示原因，
            // 让调用方看得见问题，而不是拿到一个闪退的窗口。
            LastNavigationError = $"{exception.GetType().Name}: {exception.Message}";
            ShowInitializationFailure(exception);
        }
        finally
        {
            _initializationCancellation?.Dispose();
            _initializationCancellation = null;
        }

        if (!cancelled && _openedAsync is not null && !IsClosed)
        {
            await _openedAsync(this);
        }
    }

    /// <summary>创建 controller、挂接事件并完成首次导航。</summary>
    private async Task InitializeWebViewAsync(CancellationToken token)
    {
        await _webViewInitialization.RunInteractiveAsync(
            () => _webView.EnsureCoreWebView2Async(_environment, _controllerOptions),
            token);
        var core = _webView.CoreWebView2;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.NewWindowRequested += OnNewWindowRequested;
        core.PermissionRequested += (_, eventArgs) => eventArgs.State = CoreWebView2PermissionState.Deny;
        core.DownloadStarting += (_, eventArgs) => eventArgs.Cancel = true;
        core.NavigationStarting += (_, eventArgs) =>
        {
            if (_allowedOrigins.Count == 0) return;
            if (!Uri.TryCreate(eventArgs.Uri, UriKind.Absolute, out var target)
                || !_allowedOrigins.Contains(GetOrigin(target)))
            {
                eventArgs.Cancel = true;
            }
        };
        await core.AddScriptToExecuteOnDocumentCreatedAsync(
            WebViewPresentationMask.CreateInitializationScript(_options.BackgroundColor));
        await core.AddScriptToExecuteOnDocumentCreatedAsync(WebViewWindowBridge.CreateScript(Id));
        core.WebMessageReceived += (_, eventArgs) => _ = HandleBridgeMessageAsync(eventArgs.WebMessageAsJson);

        var targetUrl = _options.Url;
        var initialPresentationCompleted = false;
        core.NavigationCompleted += (_, eventArgs) =>
        {
            // 成功导航即清除上一次的失败记录，使 LastNavigationError 反映「当前是否正常」
            // 而非「历史上是否失败过」——否则一次失败会永久留在属性上误导宿主。
            //
            // 刻意【不】在 !IsSuccess 时写入错误：官方文档明确 IsSuccess 为 false 也可能是
            // 非灾难性情形（页面主动跳转、window.stop()、被 AllowedOrigins 主动拦截、
            // 应用自行取消导航），一律记为失败会产生大量误导信息。
            // 真正的失败记录点仍在首次导航（见下方 initialNavigation 处理）。
            if (eventArgs.IsSuccess)
            {
                LastNavigationError = null;
            }

            // 无论成败都必须揭罩。NavigationCompleted 的定义是「页面完全加载（body.onload）
            // 或加载因错误而停止」，失败时 WebView 显示的是错误页——它同样是可见内容。
            // 若这里附加 IsSuccess 条件（曾经如此），加载失败（断网／404／超时）会让全屏且
            // pointer-events:auto 的遮罩永久留在页面上：窗口停在转圈界面且鼠标点不进去，
            // 只能关闭重开。失败原因另有 LastNavigationError 记录，不需要靠遮罩表达。
            if (initialPresentationCompleted)
            {
                _ = RevealCompletedNavigationAsync(core);
            }
        };

        if (targetUrl is not null)
        {
            var initialNavigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnInitialNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
            {
                // 首次导航的完成回调可能对应重定向链上的任意一跳，也可能对应页面内的
                // SPA 路由。不能用 URL 比对判断"是不是我请求的那次导航"——重定向会让
                // 地址变化，比对失败就一直等下去。这里接受第一个到达的完成回调。
                initialNavigation.TrySetResult(eventArgs.IsSuccess);
            }

            core.NavigationCompleted += OnInitialNavigationCompleted;
            try
            {
                _webView.Source = targetUrl;
                // 首次导航失败（网络不可达、DNS 失败、超时）不抛异常：窗口本身已经可用，
                // 调用方可重试，失败原因记录在 LastNavigationError。
                if (!await initialNavigation.Task.WaitAsync(TimeSpan.FromSeconds(30), token))
                {
                    LastNavigationError = "首次导航失败。";
                }
            }
            catch (TimeoutException)
            {
                LastNavigationError = "首次导航超时。";
            }
            finally
            {
                core.NavigationCompleted -= OnInitialNavigationCompleted;
            }
        }

        await RevealCompletedNavigationAsync(core, token);
        initialPresentationCompleted = true;
        _initializationSurface.Visibility = Visibility.Collapsed;
        _webView.Visibility = Visibility.Visible;
        UpdateLayout();
        await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render, token);
        if (_options.Focus) Activate();
    }

    /// <summary>把 WebView2 初始化失败的原因显示在窗口内容区，替代加载指示。</summary>
    private void ShowInitializationFailure(Exception exception) =>
        WebViewWindowPresentation.ShowFailure(_initializationSurface, _webView, exception);

    /// <summary>
    /// 以新的设置原位更新窗口外观（标题、尺寸、最小尺寸、置顶、任务栏、可调整大小、标题栏样式与配色）。
    /// </summary>
    /// <param name="options">新的设置。</param>
    /// <remarks>
    /// <para><b>不会触发导航</b>：<see cref="WebViewWindowOptions.Url"/> 在此被忽略，
    /// 需要跳转时请另行调用 <see cref="NavigateAsync"/>。</para>
    /// <para><b>会同步生效的还有</b>：<see cref="WebViewWindowOptions.AllowedOrigins"/>
    /// （替换同源白名单）。</para>
    /// <para><b>不会生效的</b>：<see cref="WebViewWindowOptions.ClosingAsync"/> 等回调在构造时
    /// 已捕获，此处替换设置不影响它们。</para>
    /// </remarks>
    public void ApplyOptions(WebViewWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _allowedOrigins = NormalizeOrigins(options.AllowedOrigins);
        Title = options.Title ?? options.Url?.Host ?? string.Empty;
        ResizeMode = options.Resizable ? ResizeMode.CanResize : ResizeMode.NoResize;
        if (IsFinitePositive(options.Width)) Width = options.Width;
        if (IsFinitePositive(options.Height)) Height = options.Height;
        MinWidth = Math.Max(0, options.MinWidth);
        MinHeight = Math.Max(0, options.MinHeight);
        Topmost = options.Topmost;
        ShowInTaskbar = options.ShowInTaskbar;
        TitleBarStyle = options.TitleBarStyle;
        TitleBarPalette = options.TitleBarPalette;
        ApplyBackgroundColor(options.BackgroundColor);
    }

    /// <summary>
    /// 把底色贯通到三处显示层：WebView 无内容时的底色、窗口内容根、以及 WPF 加载面板。
    /// </summary>
    /// <remarks>
    /// <para>三者必须一致，否则未加载出内容的那一段时间会露出与页面不同的颜色——
    /// 深色页面配浅色底即表现为"白闪"。</para>
    /// <para>注入遮罩的底色在 <see cref="InitializeWebViewAsync"/> 生成脚本时取用
    /// （脚本一次性注入，无法在此处更新）。</para>
    /// </remarks>
    private void ApplyBackgroundColor(Color color)
    {
        _webView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        _presentationRoot.Background = brush;
        _initializationSurface.Background = brush;
        // 深色底需要更亮的前景色，否则"正在加载…"文字在深色上看不清。
        if (_initializationSurface.Child is Panel panel)
        {
            foreach (var child in panel.Children.OfType<TextBlock>())
            {
                child.Foreground = new SolidColorBrush(WebViewPresentationMask.PickForeground(color));
            }
        }
    }

    /// <summary>导航到绝对地址，受 <see cref="WebViewWindowOptions.AllowedOrigins"/> 约束。</summary>
    /// <param name="target">目标绝对地址。</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> 为 <c>null</c>。</exception>
    /// <exception cref="InvalidOperationException">目标 Origin 不在允许列表内。</exception>
    /// <remarks>
    /// <para><b>本方法立即返回，不等待导航完成</b>——它只是发起导航。
    /// 需要感知加载结果时，请自行在页面内监听，或依赖 <see cref="LastNavigationError"/>。</para>
    /// <para>白名单为空时不限制；非空时在此处<b>主动校验并抛异常</b>，
    /// 而 WebView2 侧的 <c>NavigationStarting</c> 另有拦截作为第二道防线
    /// （覆盖页面内部发起的跳转）。</para>
    /// </remarks>
    public Task NavigateAsync(Uri target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_allowedOrigins.Count > 0 && !_allowedOrigins.Contains(GetOrigin(target)))
        {
            throw new InvalidOperationException("导航目标不在允许的 Origin 中。");
        }

        _webView.Source = target;
        return Task.CompletedTask;
    }

    /// <summary>重新加载当前页面。</summary>
    /// <remarks>
    /// <para><b>立即返回，不等待加载完成。</b></para>
    /// <para>重载会触发注入脚本重新执行，因此加载遮罩会再次出现并在导航完成后揭开。
    /// 若目标页面为深色而 <see cref="WebViewWindowOptions"/> 未指定匹配的底色，
    /// 重载瞬间可能出现白色闪烁。</para>
    /// </remarks>
    public Task ReloadAsync()
    {
        _webView.Reload();
        return Task.CompletedTask;
    }

    /// <summary>设置窗口标题。</summary>
    /// <remarks>
    /// 等价于直接给继承自 <see cref="ChromeWindow"/> 的 <c>Title</c> 属性赋值；
    /// <paramref name="title"/> 为 <c>null</c> 时置为空字符串。
    /// </remarks>
    public Task SetTitleAsync(string title)
    {
        Title = title ?? string.Empty;
        return Task.CompletedTask;
    }

    /// <summary>显示窗口、约束到当前工作区并置于前台。</summary>
    /// <remarks>
    /// 与继承自 <see cref="ChromeWindow"/> 的 <c>Show()</c> 相比，
    /// 额外做了工作区约束与激活，适合窗口曾被移到已拔掉的显示器后重新显示的场景。
    /// </remarks>
    public Task ShowWindowAsync()
    {
        Show();
        WindowPlacementService.ConstrainToWorkArea(this);
        Activate();
        return Task.CompletedTask;
    }

    /// <summary>隐藏窗口，但保留 HWND、WebView2 实例与页面状态（不触发卸载）。</summary>
    /// <remarks>与 <see cref="CloseAsync"/> 不同：隐藏后窗口仍在注册表中，可再次显示。</remarks>
    public Task HideAsync()
    {
        Hide();
        return Task.CompletedTask;
    }

    /// <summary>最小化窗口。</summary>
    /// <remarks>等价于设置继承自 <see cref="ChromeWindow"/> 的 <c>WindowState</c>；不改变 <see cref="IsClosed"/>。</remarks>
    public Task MinimizeAsync()
    {
        WindowState = WindowState.Minimized;
        return Task.CompletedTask;
    }

    /// <summary>最大化窗口。</summary>
    /// <remarks>等价于设置继承自 <see cref="ChromeWindow"/> 的 <c>WindowState</c>；
    /// 当 <see cref="WebViewWindowOptions.Resizable"/> 为 <c>false</c> 时最大化为无效操作。</remarks>
    public Task MaximizeAsync()
    {
        WindowState = WindowState.Maximized;
        return Task.CompletedTask;
    }

    /// <summary>从最大化或最小化状态恢复到普通状态，并把窗口约束回当前工作区。</summary>
    /// <remarks>
    /// 比单独设置 <c>WindowState = Normal</c> 多一步工作区约束，
    /// 用于避免窗口恢复后停留在已拔掉的显示器上而不可见。
    /// <para><b>与页面桥的差异</b>：页面脚本的 <c>browserShell.window.restore()</c>
    /// 当前<b>只</b>设置窗口状态、不执行工作区约束，两者行为不完全一致。</para>
    /// </remarks>
    public Task RestoreAsync()
    {
        WindowState = WindowState.Normal;
        WindowPlacementService.ConstrainToWorkArea(this);
        return Task.CompletedTask;
    }

    /// <summary>请求关闭窗口；是否真正关闭由 <see cref="WebViewWindowOptions.ClosingAsync"/> 裁决。</summary>
    /// <param name="source">关闭来源标识，会作为 <see cref="WebViewWindowClosingContext.Source"/>
    /// 传给裁决回调。宿主可传自定义值，以便业务区分主动关闭与用户操作。</param>
    /// <remarks>
    /// <para><b>可能被拒绝</b>：提供了 <c>ClosingAsync</c> 且其返回 <c>false</c> 时，窗口保持打开。
    /// 此时窗口<b>仍保留在 <see cref="WebViewShell"/> 的注册表中</b>，
    /// <see cref="IsClosed"/> 保持 <c>false</c>。</para>
    /// <para><b>幂等</b>：窗口已关闭时直接返回，不抛异常。</para>
    /// <para>未提供 <c>ClosingAsync</c> 时直接关闭，不经过裁决。</para>
    /// <para>关闭真正发生后，所有者先将其移出注册表，再回调
    /// <see cref="WebViewWindowOptions.ClosedAsync"/>（因此回调内读到的窗口计数已更新）。</para>
    /// </remarks>
    public Task CloseAsync(string source = "Service")
    {
        if (IsClosed) return Task.CompletedTask;
        if (_closingAsync is null)
        {
            return RunOnUiThreadAsync(() =>
            {
                CloseCore();
                return Task.CompletedTask;
            });
        }

        return RunOnUiThreadAsync(() => RequestCloseAsync(source, pageRequestId: null));
    }

    /// <summary>
    /// <b>不经过关闭裁决</b>直接关闭窗口。
    /// </summary>
    /// <remarks>
    /// <para>用于宿主确知必须无条件关闭的场景（例如整体退出、紧急回收）。
    /// 常规关闭请用 <see cref="CloseAsync"/>，以便业务方有机会拦截。</para>
    /// <para>该调用同样会触发 <see cref="WebViewWindowOptions.ClosedAsync"/>
    /// 并将窗口移出注册表；已关闭时为无操作。</para>
    /// </remarks>
    public void ClosePermanently() => RunOnUiThread(CloseCore);

    private void OnDisplayConfigurationChanged(object? sender, EventArgs eventArgs)
    {
        if (IsClosed) return;
        try
        {
            WindowPlacementService.ConstrainToWorkArea(this);
        }
        catch (Exception)
        {
            // 显示器热插拔期间的瞬时失败不应影响窗口存续。
        }
    }

    private void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (_forceClose) return;
        eventArgs.Cancel = true;
        _ = RequestCloseAsync("WindowChrome", pageRequestId: null);
    }

    private async Task RequestCloseAsync(string source, string? pageRequestId)
    {
        if (_closePending)
        {
            if (pageRequestId is not null)
            {
                PostCloseResult(pageRequestId, accepted: false, "已有关闭请求正在裁决。", "CLOSE_PENDING");
            }

            return;
        }

        _closePending = true;
        try
        {
            var accepted = _closingAsync is null
                || await _closingAsync(new WebViewWindowClosingContext(this, source), CancellationToken.None);
            if (accepted)
            {
                CloseCore();
            }

            if (pageRequestId is not null)
            {
                PostCloseResult(
                    pageRequestId,
                    accepted,
                    accepted ? null : "窗口拒绝关闭。",
                    accepted ? null : "REJECTED");
            }
        }
        catch (Exception)
        {
            if (pageRequestId is not null)
            {
                PostCloseResult(pageRequestId, accepted: false, "关闭请求处理失败。", "HANDLER_FAILED");
            }
        }
        finally
        {
            _closePending = false;
        }
    }

    /// <summary>
    /// 处理页面桥发来的消息。页面可发送<b>任意</b>内容，因此本方法必须容忍一切输入。
    /// </summary>
    /// <remarks>
    /// <para>这里曾是无防护的 fire-and-forget，而 <c>System.Text.Json</c> 有三处会抛
    /// <see cref="InvalidOperationException"/>／<see cref="JsonException"/>：</para>
    /// <list type="bullet">
    ///   <item><description><c>JsonDocument.Parse</c> 对非 JSON 文本抛 <c>JsonException</c>；</description></item>
    ///   <item><description><c>TryGetProperty</c> 在根节点<b>不是对象</b>时抛 <c>InvalidOperationException</c>
    ///   （页面发 <c>postMessage(42)</c> 即触发）；</description></item>
    ///   <item><description><c>GetString()</c> 在值<b>不是字符串</b>时抛 <c>InvalidOperationException</c>
    ///   （如 <c>{"type":42}</c>、<c>{"type":"shellWindow","operation":42}</c>）。</description></item>
    /// </list>
    /// <para>上述均为已实测确认的行为。若不拦截，异常会成为未观察任务异常。
    /// 注意 <c>AreHostObjectsAllowed = false</c> <b>挡不住</b>这条通道——它是宿主通信通道，
    /// 不属于 host objects。</para>
    /// <para>未知或畸形消息一律<b>静默忽略</b>：这是页面可控的输入，不应影响窗口功能。</para>
    /// </remarks>
    private async Task HandleBridgeMessageAsync(string json)
    {
        try
        {
            await HandleBridgeMessageCoreAsync(json);
        }
        catch (Exception)
        {
            // 页面发来的任意内容都不应影响宿主：解析失败、类型不符、以及处理器自身
            // （如宿主的 ClosingAsync）抛出的异常，全部在此隔离。
        }
    }

    private async Task HandleBridgeMessageCoreAsync(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        // 根节点不是对象时 TryGetProperty 会抛，必须先判断。
        if (root.ValueKind != JsonValueKind.Object) return;
        if (!TryGetString(root, "type", out var messageType) || messageType != "shellWindow") return;
        if (!TryGetString(root, "operation", out var operation)) return;
        switch (operation)
        {
            case "minimize": WindowState = WindowState.Minimized; break;
            case "maximize": WindowState = WindowState.Maximized; break;
            case "restore": WindowState = WindowState.Normal; break;
            case "close":
                TryGetString(root, "requestId", out var requestId);
                await RequestCloseAsync("Page", requestId);
                break;
        }
    }

    private void PostCloseResult(string requestId, bool accepted, string? message, string? code) =>
        _webView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "shellWindowCloseResult",
            requestId,
            accepted,
            code,
            message,
        }));

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        if (Uri.TryCreate(eventArgs.Uri, UriKind.Absolute, out var target)
            && (_allowedOrigins.Count == 0 || _allowedOrigins.Contains(GetOrigin(target))))
        {
            if (_newWindowAsync is not null) _ = _newWindowAsync(this, target);
            else _ = NavigateAsync(target);
        }
    }

    private async Task RevealCompletedNavigationAsync(CoreWebView2 core, CancellationToken token = default)
    {
        try
        {
            var generation = Interlocked.Increment(ref _navigationGeneration);
            await WebViewPresentationMask.SetVisibilityAsync(core, visible: false, generation, token);
        }
        catch (Exception)
        {
            // 遮罩不可用时页面仍然可见，不中断导航。
        }
    }

    /// <summary>
    /// 触发关闭通知（所有者注销 + 宿主回调），保证只执行一次。
    /// </summary>
    /// <remarks>
    /// <para>返回的 Task 可被多个调用者 await：关闭通知有两条触发路径——<c>Closed</c> 事件的
    /// fire-and-forget 调用，以及 <see cref="DisposeAsync"/>。若只用一个 bool 做一次性守卫，
    /// 后到的 <see cref="DisposeAsync"/> 会因守卫已置位而立即返回，
    /// 导致它在宿主的 <c>ClosedAsync</c> 尚未跑完时就宣告完成。</para>
    /// <para>回调在 UI 线程上触发：宿主在 <c>ClosedAsync</c> 中常会读取
    /// <c>WindowCount</c>／<c>Windows</c> 或操作窗口，这些都不是线程安全的。</para>
    /// </remarks>
    private Task NotifyClosedOnceAsync()
    {
        // 快速路径：通知已启动则直接复用其 Task，避免在 Dispatcher 关闭后再排队。
        if (Volatile.Read(ref _closedNotification) is { } started)
        {
            return started;
        }

        return MarshalAsync(() =>
        {
            // 在 UI 线程上读改写，天然串行。
            if (Volatile.Read(ref _closedNotification) is { } existing)
            {
                return existing;
            }

            // 先登记 Task 再跑回调：若宿主在 ClosedAsync 里回调 DisposeAsync（重入），
            // 上面两条快速路径会命中已登记的 Task，而不会再次触发回调造成递归。
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _closedNotification, completion.Task);
            // IsClosed 先于回调确立：宿主在回调里读到的状态应与"已关闭"一致。
            Volatile.Write(ref _closedOnceFlag, 1);
            _ = RunClosedCallbacksAsync(completion);
            return completion.Task;
        });
    }

    private async Task RunClosedCallbacksAsync(TaskCompletionSource completion)
    {
        Volatile.Write(ref _closedCallbackThreadId, Environment.CurrentManagedThreadId);
        try
        {
            // 先让所有者把本窗口移出注册表，再回调用户。
            // 顺序不可颠倒：ClosedAsync 里常会读取 WindowCount / Windows（示例即如此），
            // 若此时本窗口仍在注册表中，宿主看到的窗口数会多算一个。
            if (_onClosed is not null) await _onClosed(this);
            if (_closedAsync is not null) await _closedAsync(this);
            completion.SetResult();
        }
        catch (Exception exception)
        {
            // 异常经 Task 传递给等待者（DisposeAsync）；fire-and-forget 路径上
            // 无人观察时也不会逃逸为未处理异常而终止进程。
            completion.SetException(exception);
        }
        finally
        {
            Volatile.Write(ref _closedCallbackThreadId, 0);
        }
    }

    private void CloseCore()
    {
        if (IsClosed) return;
        _forceClose = true;
        _initializationCancellation?.Cancel();
        if (IsVisible) Hide();
        Owner = null;
        Close();
    }

    /// <summary>
    /// 关闭窗口并释放 WebView2 资源。<b>不经过关闭裁决</b>。
    /// </summary>
    /// <remarks>
    /// <para>与 <see cref="CloseAsync"/> 的区别：本方法是"强制清理"，忽略
    /// <see cref="WebViewWindowOptions.ClosingAsync"/> 的裁决结果。</para>
    /// <para>可由任意线程调用：内部会切回 UI 线程维护注册表。
    /// 重复调用是安全的（窗口与 WebView2 的释放各自幂等）。</para>
    /// <para>通常由 <see cref="WebViewShell.DisposeAsync"/> 统一调用，宿主一般无需直接使用。</para>
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (!IsClosed)
        {
            await RunOnUiThreadAsync(() =>
            {
                CloseCore();
                return Task.CompletedTask;
            });
        }

        // 无条件等待关闭通知：若窗口是经标题栏正常关闭的，Closed 事件已启动通知，
        // 此处复用同一个 Task 并等待其完成——否则 DisposeAsync 会在宿主的 ClosedAsync
        // 尚未跑完时就返回（这正是「先注销再回调」顺序要保障的可见性）。
        //
        // 但若本方法正是从宿主的 ClosedAsync 回调内部被调用的（重入），等待该通知会自锁：
        // 回调在等本方法返回，而本方法在等回调完成。此时跳过等待——关闭通知本就已在执行中。
        //
        // 该判断是**保守**的：它无法区分「回调内部的重入调用」与「回调 await 期间
        // UI 线程上的另一次调用」。后者其实不会死锁（回调并未等待该调用者），
        // 但也会一并跳过等待。代价仅是 DisposeAsync 可能早于 ClosedAsync 返回；
        // 反向的误判（该跳过却没跳过）才会死锁，这里确保了不会发生。
        if (Volatile.Read(ref _closedCallbackThreadId) != Environment.CurrentManagedThreadId)
        {
            await NotifyClosedOnceAsync();
        }

        DisposeWebViewOnce();
    }

    private void DisposeWebViewOnce()
    {
        if (Interlocked.Exchange(ref _webViewDisposed, 1) == 0)
        {
            // WebView2 派生自 HwndHost，其 Dispose(bool) 内部同样 VerifyAccess()，
            // 因此释放也必须回到 UI 线程——即使 DisposeAsync 是从后台线程调用的。
            // 用 Marshal 而非 RunOnUiThread：释放是清理动作，恰恰在窗口已关闭时最需要执行，
            // 若沿用「已关闭则跳过」的守卫会造成 WebView2 泄漏。
            Marshal(() => _webView.Dispose());
        }
    }

    private static HashSet<string> NormalizeOrigins(IReadOnlyList<string> origins) =>
        new(origins ?? [], StringComparer.OrdinalIgnoreCase);

    private static bool IsFinitePositive(double value) => double.IsFinite(value) && value > 0;

    private static string GetOrigin(Uri uri) => uri.GetLeftPart(UriPartial.Authority);

    /// <summary>
    /// 读取对象的字符串属性；属性缺失或值不是字符串时返回 <c>false</c> 而非抛异常。
    /// </summary>
    /// <remarks>页面的输入不可信，<c>JsonElement.GetString()</c> 对非字符串值会抛
    /// <see cref="InvalidOperationException"/>，故统一经此读取。</remarks>
    private static bool TryGetString(JsonElement element, string propertyName, [NotNullWhen(true)] out string? value)
    {
        value = null;
        if (element.ValueKind != JsonValueKind.Object) return false;
        if (!element.TryGetProperty(propertyName, out var property)) return false;
        if (property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString();
        return value is not null;
    }

    /// <summary>
    /// 把操作封送回 UI 线程执行；已在 UI 线程时同步直接执行，避免无谓的队列往返。
    /// <b>已关闭时跳过</b>——供不希望在窗口销毁后仍排队工作的公开成员使用。
    /// </summary>
    /// <remarks>WPF 的 <c>Window</c>（<c>Show</c>／<c>Hide</c>／<c>Close</c>）与 <c>WebView2</c>（派生自
    /// <c>HwndHost</c>）都是 <c>DispatcherObject</c>，其内部 <c>VerifyAccess()</c> 会对错误线程抛
    /// <see cref="InvalidOperationException"/>。因此凡承诺「可从任意线程调用」的成员都必须经此封送。</remarks>
    private Task RunOnUiThreadAsync(Func<Task> action) =>
        IsClosed ? Task.CompletedTask : MarshalAsync(action);

    private void RunOnUiThread(Action action)
    {
        if (IsClosed) return;
        Marshal(action);
    }

    /// <summary>无条件封送回 UI 线程（<b>不</b>因已关闭而跳过）。</summary>
    /// <remarks>关闭清理与关闭回调必须用本方法：它们恰恰在窗口已关闭时才需要执行，
    /// 若沿用「已关闭则跳过」的守卫，会导致 WebView2 泄漏、宿主的关闭回调永不触发。</remarks>
    private Task MarshalAsync(Func<Task> action) =>
        Dispatcher.CheckAccess() ? action() : Dispatcher.InvokeAsync(action).Task.Unwrap();

    private void Marshal(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.Invoke(action);
    }
}
