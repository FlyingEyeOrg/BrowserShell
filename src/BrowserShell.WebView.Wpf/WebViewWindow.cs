using System.ComponentModel;
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
/// <para><b>线程模型</b>：本类型所有成员必须在创建它的 <c>Dispatcher</c>（UI 线程）上调用。
/// 唯一的例外是 <see cref="CloseAsync"/> 与 <see cref="DisposeAsync"/> 可从任意线程调用，
/// 其内部关闭流程会经所有者回调切回 UI 线程维护注册表。</para>
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
    private readonly Grid _presentationRoot = new() { Background = Brushes.White };
    private readonly Border _initializationSurface = CreateInitializationSurface();
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
    private bool _closedOnce;
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
    public bool IsClosed => _closedOnce;

    /// <summary>
    /// 最近一次导航的失败原因；为 <c>null</c> 表示无已知失败。
    /// </summary>
    /// <remarks>
    /// <para>下列情形会写入该值：</para>
    /// <list type="bullet">
    ///   <item><description>WebView2 环境/Controller 初始化失败（格式为 <c>异常类型: 消息</c>）；</description></item>
    ///   <item><description>首次导航失败（网络不可达、DNS 失败等），值为"首次导航失败。"；</description></item>
    ///   <item><description>首次导航超时（30 秒），值为"首次导航超时。"。</description></item>
    /// </list>
    /// <para><b>注意</b>：当前实现<b>不会在导航成功时重置</b>该属性，因此它表示
    /// "曾经发生过失败"而非"当前处于失败状态"。调用方若需判断当前状态，
    /// 应结合页面实际加载结果，或等待后续版本重置语义。</para>
    /// <para>首次导航失败不会抛异常：窗口本身已可用，调用方可自行重试
    /// （例如再次调用 <see cref="ReloadAsync"/>）。</para>
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
    public async Task InitializeAndShowAsync(CancellationToken token = default)
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

        if (!cancelled && _openedAsync is not null && !_closedOnce)
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
        await core.AddScriptToExecuteOnDocumentCreatedAsync(WebViewPresentationMask.InitializationScript);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(CreateWindowBridgeScript(Id));
        core.WebMessageReceived += (_, eventArgs) => _ = HandleBridgeMessageAsync(eventArgs.WebMessageAsJson);

        var targetUrl = _options.Url;
        var initialPresentationCompleted = false;
        core.NavigationCompleted += (_, eventArgs) =>
        {
            if (initialPresentationCompleted && eventArgs.IsSuccess)
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
    private void ShowInitializationFailure(Exception exception)
    {
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
        _initializationSurface.Child = content;
        _initializationSurface.Visibility = Visibility.Visible;
        _webView.Visibility = Visibility.Collapsed;
    }

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
    public async Task CloseAsync(string source = "Service")
    {
        if (_closedOnce) return;
        if (_closingAsync is null)
        {
            CloseCore();
            return;
        }

        await RequestCloseAsync(source, pageRequestId: null);
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
    public void ClosePermanently() => CloseCore();

    private void OnDisplayConfigurationChanged(object? sender, EventArgs eventArgs)
    {
        if (_closedOnce) return;
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

    private async Task HandleBridgeMessageAsync(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("type", out var type) || type.GetString() != "shellWindow") return;
        var operation = root.TryGetProperty("operation", out var operationElement)
            ? operationElement.GetString()
            : null;
        switch (operation)
        {
            case "minimize": WindowState = WindowState.Minimized; break;
            case "maximize": WindowState = WindowState.Maximized; break;
            case "restore": WindowState = WindowState.Normal; break;
            case "close":
                var requestId = root.TryGetProperty("requestId", out var requestIdElement)
                    ? requestIdElement.GetString()
                    : null;
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

    private async Task NotifyClosedOnceAsync()
    {
        if (_closedOnce) return;
        _closedOnce = true;
        // 先让所有者把本窗口移出注册表，再回调用户。
        // 顺序不可颠倒：ClosedAsync 里常会读取 WindowCount / Windows（示例即如此），
        // 若此时本窗口仍在注册表中，宿主看到的窗口数会多算一个。
        if (_onClosed is not null) await _onClosed(this);
        if (_closedAsync is not null) await _closedAsync(this);
    }

    private void CloseCore()
    {
        if (_closedOnce) return;
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
        if (!_closedOnce)
        {
            CloseCore();
            await NotifyClosedOnceAsync();
        }

        DisposeWebViewOnce();
    }

    private void DisposeWebViewOnce()
    {
        if (Interlocked.Exchange(ref _webViewDisposed, 1) == 0)
        {
            _webView.Dispose();
        }
    }

    private static HashSet<string> NormalizeOrigins(IReadOnlyList<string> origins) =>
        new(origins ?? [], StringComparer.OrdinalIgnoreCase);

    private static bool IsFinitePositive(double value) => double.IsFinite(value) && value > 0;

    private static string GetOrigin(Uri uri) => uri.GetLeftPart(UriPartial.Authority);

    /// <summary>注入页面侧窗口控制桥，供页面脚本最小化、最大化、还原与请求关闭。</summary>
    private static string CreateWindowBridgeScript(string windowId)
    {
        var windowIdJson = JsonSerializer.Serialize(windowId);
        return $$"""
        (() => {
          const send = (operation, value, requestId) => chrome.webview.postMessage({
            type: 'shellWindow', operation, value: value ?? null, requestId: requestId ?? null
          });
          const closeRequests = new Map();
          chrome.webview.addEventListener('message', event => {
            const message = event.data;
            if (message?.type !== 'shellWindowCloseResult') return;
            const resolve = closeRequests.get(message.requestId);
            if (!resolve) return;
            closeRequests.delete(message.requestId);
            resolve(Object.freeze({
              accepted: message.accepted === true,
              code: message.code ?? null,
              message: message.message ?? null
            }));
          });
          const current = globalThis.browserShell ?? {};
          Object.defineProperty(globalThis, 'browserShell', {
            configurable: true,
            value: Object.freeze({ ...current, window: Object.freeze({
              windowId: {{windowIdJson}},
              minimize: () => send('minimize'),
              maximize: () => send('maximize'),
              restore: () => send('restore'),
              close: () => new Promise(resolve => {
                const requestId = crypto.randomUUID();
                closeRequests.set(requestId, resolve);
                send('close', 'Page', requestId);
              })
            }) })
          });
        })();
        """;
    }

    private static Border CreateInitializationSurface()
    {
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
            Foreground = Brushes.DimGray,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(progress);
        content.Children.Add(label);
        return new Border { Background = Brushes.White, Child = content };
    }
}
