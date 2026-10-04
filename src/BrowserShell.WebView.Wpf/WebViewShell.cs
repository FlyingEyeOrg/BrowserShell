using System.Windows.Threading;

namespace BrowserShell.WebView.Wpf;

/// <summary>
/// 在单个 WPF Dispatcher 上创建并跟踪 Web 外壳窗口的入口。
/// 只管理窗口生命周期，不涉及任何业务协议或远程会话。
/// </summary>
public sealed class WebViewShell : IAsyncDisposable
{
    private readonly WebViewWindowEnvironment _environment;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<string, WebViewWindow> _windows = new(StringComparer.Ordinal);
    private bool _disposed;

    private WebViewShell(WebViewWindowEnvironment environment, Dispatcher dispatcher)
    {
        _environment = environment;
        _dispatcher = dispatcher;
    }

    /// <summary>创建外壳管理器，并初始化进程内唯一的 WebView2 环境。</summary>
    /// <param name="dispatcher">承载窗口的 WPF Dispatcher，通常是 <c>Application.Current.Dispatcher</c>。</param>
    /// <param name="browserExecutableFolder">固定 WebView2 Runtime 目录；为 null 时使用系统 Evergreen Runtime。</param>
    /// <param name="userDataFolder">自定义用户数据目录；为 null 时使用进程临时目录。</param>
    /// <param name="token">取消令牌。</param>
    public static async Task<WebViewShell> CreateAsync(
        Dispatcher dispatcher,
        string? browserExecutableFolder = null,
        string? userDataFolder = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        var environment = WebViewWindowEnvironment.Create(dispatcher, browserExecutableFolder, userDataFolder);
        await environment.InitializeAsync(token);
        return new WebViewShell(environment, dispatcher);
    }

    /// <summary>当前打开的窗口数量。</summary>
    public int WindowCount => _windows.Count;

    /// <summary>当前所有窗口。</summary>
    public IReadOnlyCollection<WebViewWindow> Windows => _windows.Values;

    /// <summary>按标识查找窗口。</summary>
    public bool TryGetWindow(string id, out WebViewWindow? window)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _windows.TryGetValue(id, out window);
    }

    /// <summary>创建并显示一个 Web 外壳窗口。</summary>
    /// <param name="options">窗口设置。</param>
    /// <param name="id">窗口标识；为 null 时自动生成。</param>
    /// <param name="token">取消令牌。</param>
    public async Task<WebViewWindow> OpenAsync(
        WebViewWindowOptions options,
        string? id = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var windowId = id ?? Guid.NewGuid().ToString("N");
        if (_windows.ContainsKey(windowId))
        {
            throw new InvalidOperationException($"窗口标识 {windowId} 已存在。");
        }

        var window = await _dispatcher.InvokeAsync(() =>
        {
            var created = new WebViewWindow(
                windowId,
                options,
                _environment.Core,
                _environment.CreateControllerOptions(windowId),
                _environment.InitializationCoordinator);
            _windows[windowId] = created;
            return created;
        });

        try
        {
            await window.InitializeAndShowAsync(token);
        }
        catch
        {
            _windows.Remove(windowId);
            await window.DisposeAsync();
            throw;
        }

        return window;
    }

    /// <summary>关闭指定窗口并从管理器中移除。</summary>
    public async Task CloseAsync(string id, string source = "Service")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!_windows.TryGetValue(id, out var window))
        {
            return;
        }

        await window.CloseAsync(source);
        _windows.Remove(id);
    }

    /// <summary>释放所有窗口与 WebView2 环境。</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var window in _windows.Values.ToArray())
        {
            await window.DisposeAsync();
        }

        _windows.Clear();
        await _environment.DisposeAsync();
    }
}
