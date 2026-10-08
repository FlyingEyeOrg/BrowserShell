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
    // 0 = 可用，1 = 正在释放/已释放。用 int 以便 Interlocked 做一次性转移。
    private int _disposeStarted;

    private bool IsDisposed => Volatile.Read(ref _disposeStarted) != 0;

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
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (id is not null)
        {
            // null 表示「自动生成」，非 null 才需要校验；白空字符串是调用方的错误。
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
        }

        var windowId = id ?? Guid.NewGuid().ToString("N");
        var window = await _dispatcher.InvokeAsync(() =>
        {
            // 释放可能在本方法 await 期间发生，因此必须在 UI 线程回调内重新确认一次，
            // 否则会把窗口注册进一个已释放的 shell。
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            // 查重与注册必须在同一个 UI 线程回调内完成。OpenAsync 允许从任意线程调用，
            // 若把查重放在 await 之前，两个并发调用会同时通过检查、随后在注册时互相覆盖，
            // 结果是两次都成功返回、却有一个窗口不在注册表中（孤儿窗口）。
            if (_windows.ContainsKey(windowId))
            {
                throw new InvalidOperationException($"窗口标识 {windowId} 已存在。");
            }

            var created = new WebViewWindow(
                windowId,
                options,
                _environment.Core,
                _environment.CreateControllerOptions(windowId),
                _environment.InitializationCoordinator,
                OnWindowClosed);
            // 用 Add 而非索引器：索引器会把已注册的同名窗口静默顶掉。
            _windows.Add(windowId, created);
            return created;
        });

        try
        {
            // InitializeAndShowAsync 自身会封送回 UI 线程（其第一步是 EnsureHandle()/Show()，
            // 二者都经 DispatcherObject.VerifyAccess() 校验线程），因此这里可直接 await，
            // 无需再包一层 InvokeAsync。
            await window.InitializeAndShowAsync(token);
        }
        catch
        {
            // 窗口创建/初始化本身失败：此时窗口不可用，必须回收，否则会残留一个
            // 没有 WebView 的空壳窗口。DisposeAsync 内部会经 OnWindowClosed 移除注册，
            // 这里再显式移除一次以覆盖「窗口尚未进入关闭流程」的路径（Remove 幂等）。
            // DisposeAsync 自身也已封送，故此处不必再切线程。
            await RemoveAsync(windowId);
            await window.DisposeAsync();
            throw;
        }

        return window;
    }

    /// <summary>请求关闭指定窗口。窗口经裁决拒绝关闭时，仍保留在管理器中。</summary>
    /// <remarks>可从任意线程调用：注册表读取会自动切回 UI 线程。</remarks>
    public async Task CloseAsync(string id, string source = "Service")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        // 在 UI 线程上读取：注册表由 UI 线程独占写入（见 OpenAsync），
        // 从其他线程直接 TryGetValue 会与增删并发，Dictionary 不是线程安全的。
        var window = await _dispatcher.InvokeAsync(
            () => _windows.TryGetValue(id, out var found) ? found : null);
        if (window is null)
        {
            return;
        }

        // CloseAsync 可能被 ClosingAsync 裁决为拒绝，此时窗口仍然存活，必须保留注册。
        // 真正关闭时由 OnWindowClosed 回调负责移除，这里不再手动 Remove——
        // 否则一个「拒绝关闭」的窗口会被移出注册表，变成无人跟踪的孤儿。
        await window.CloseAsync(source);
    }

    /// <summary>从注册表移除标识；可从任意线程调用。Remove 幂等，重复调用无副作用。</summary>
    private Task RemoveAsync(string id)
    {
        if (_dispatcher.CheckAccess())
        {
            _windows.Remove(id);
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(() => _windows.Remove(id)).Task;
    }

    /// <summary>
    /// 窗口真正关闭后的回调：从注册表移除，释放该标识。
    /// 由 <see cref="WebViewWindow"/> 在其关闭路径上调用一次，覆盖标题栏关闭、
    /// 页面桥关闭与宿主主动关闭三种来源。
    /// </summary>
    private Task OnWindowClosed(WebViewWindow window) => RemoveAsync(window.Id);

    /// <summary>释放所有窗口与 WebView2 环境。</summary>
    /// <remarks>可从任意线程调用：注册表快照在 UI 线程上获取。</remarks>
    public async ValueTask DisposeAsync()
    {
        // Interlocked 保证并发的 DisposeAsync 只有一个真正执行释放。
        if (Interlocked.Exchange(ref _disposeStarted, 1) == 1)
        {
            return;
        }

        // 先取快照再逐个释放：迭代过程中每个窗口的 DisposeAsync 会经 OnWindowClosed
        // 把自己移出注册表，直接遍历 _windows.Values 会「遍历时修改集合」。
        // 快照本身也必须在 UI 线程取，理由同 CloseAsync。
        var windows = await _dispatcher.InvokeAsync(() => _windows.Values.ToArray());
        foreach (var window in windows)
        {
            // 每个窗口的 DisposeAsync 会经 OnWindowClosed 把自己移出注册表。
            await window.DisposeAsync();
        }

        await RemoveAllAsync();
        await _environment.DisposeAsync();
    }

    /// <summary>清空注册表；可从任意线程调用。</summary>
    private Task RemoveAllAsync()
    {
        if (_dispatcher.CheckAccess())
        {
            _windows.Clear();
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(_windows.Clear).Task;
    }
}
