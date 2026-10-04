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
/// 由 <see cref="ChromeWindow"/> 提供原生窗口边框与标题栏、由 WebView2 承载页面的 Web 外壳窗口。
/// 只负责内容承载、导航与窗口级命令，不包含任何业务协议。
/// </summary>
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

    /// <summary>窗口标识。同一宿主进程内唯一。</summary>
    public string Id { get; }

    /// <summary>窗口是否已关闭。</summary>
    public bool IsClosed => _closedOnce;

    /// <summary>首次导航的失败原因；为 null 表示首次导航成功或未指定地址。</summary>
    public string? LastNavigationError { get; private set; }

    /// <summary>创建 HWND、完成首次导航并显示窗口。</summary>
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

    /// <summary>以新的设置原位更新窗口外观，需要时由调用方另行导航。</summary>
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

    /// <summary>导航到绝对地址，受 AllowedOrigins 约束。</summary>
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
    public Task ReloadAsync()
    {
        _webView.Reload();
        return Task.CompletedTask;
    }

    /// <summary>设置窗口标题。</summary>
    public Task SetTitleAsync(string title)
    {
        Title = title ?? string.Empty;
        return Task.CompletedTask;
    }

    /// <summary>显示窗口并置于前台。</summary>
    public Task ShowWindowAsync()
    {
        Show();
        WindowPlacementService.ConstrainToWorkArea(this);
        Activate();
        return Task.CompletedTask;
    }

    /// <summary>隐藏窗口但保留 HWND 与 WebView2。</summary>
    public Task HideAsync()
    {
        Hide();
        return Task.CompletedTask;
    }

    /// <summary>最小化窗口。</summary>
    public Task MinimizeAsync()
    {
        WindowState = WindowState.Minimized;
        return Task.CompletedTask;
    }

    /// <summary>最大化窗口。</summary>
    public Task MaximizeAsync()
    {
        WindowState = WindowState.Maximized;
        return Task.CompletedTask;
    }

    /// <summary>从最大化或最小化恢复。</summary>
    public Task RestoreAsync()
    {
        WindowState = WindowState.Normal;
        WindowPlacementService.ConstrainToWorkArea(this);
        return Task.CompletedTask;
    }

    /// <summary>请求关闭窗口。若设置了 ClosingAsync，由其裁决。</summary>
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

    /// <summary>不经过裁决直接关闭窗口。</summary>
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

    /// <summary>关闭窗口并释放 WebView2。</summary>
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
