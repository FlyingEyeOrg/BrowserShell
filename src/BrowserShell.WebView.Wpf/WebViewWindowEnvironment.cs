using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace BrowserShell.WebView.Wpf;

/// <summary>持有进程内唯一的 WebView2 环境，并可为不同窗口配置隔离 Profile。</summary>
public sealed class WebViewWindowEnvironment : IAsyncDisposable
{
    private readonly WebView2InitializationCoordinator _initialization;
    private readonly string? _userDataRoot;
    private bool _initialized;
    private bool _disposed;

    private WebViewWindowEnvironment(WebView2InitializationCoordinator initialization, string? userDataRoot)
    {
        _initialization = initialization;
        _userDataRoot = userDataRoot;
    }

    /// <summary>底层 WebView2 环境。初始化前访问会抛出异常。</summary>
    public CoreWebView2Environment Core =>
        _environment ?? throw new InvalidOperationException("WebView2 环境尚未初始化，请先调用 InitializeAsync。");

    private CoreWebView2Environment? _environment { get; set; }

    /// <summary>
    /// 创建环境。默认在每个进程临时目录下使用独立用户数据目录，进程退出后可直接丢弃。
    /// </summary>
    /// <param name="dispatcher">承载窗口的 WPF Dispatcher。</param>
    /// <param name="browserExecutableFolder">
    /// 固定的 WebView2 Runtime 目录。为 null 时使用系统安装的 Evergreen Runtime。
    /// </param>
    /// <param name="userDataFolder">
    /// 自定义用户数据目录。为 null 时使用临时目录；传入的目录不会在释放时删除。
    /// </param>
    public static WebViewWindowEnvironment Create(
        Dispatcher dispatcher,
        string? browserExecutableFolder = null,
        string? userDataFolder = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        var ownedRoot = userDataFolder is null
            ? Path.Combine(Path.GetTempPath(), "BrowserShell", $"{Environment.ProcessId}-{Guid.NewGuid():N}")
            : null;
        var root = userDataFolder ?? ownedRoot!;
        Directory.CreateDirectory(root);
        var environment = new WebViewWindowEnvironment(
            new WebView2InitializationCoordinator(dispatcher),
            ownedRoot)
        {
            BrowserExecutableFolder = browserExecutableFolder,
            UserDataFolder = root,
        };
        return environment;
    }

    internal string? BrowserExecutableFolder { get; private init; }

    internal string UserDataFolder { get; private init; } = string.Empty;

    /// <summary>创建一次 WebView2 环境。重复调用不产生额外开销。</summary>
    public async Task InitializeAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
        {
            return;
        }

        _environment = await CoreWebView2Environment.CreateAsync(BrowserExecutableFolder, UserDataFolder);
        token.ThrowIfCancellationRequested();
        _initialized = true;
    }

    /// <summary>
    /// 为指定标识创建隔离的 WebView2 Profile，让不同业务窗口拥有独立的 Cookie 与缓存。
    /// </summary>
    public CoreWebView2ControllerOptions CreateControllerOptions(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profileId)));
        var options = Core.CreateCoreWebView2ControllerOptions();
        // 保持 profile 路径简短，避免 WebView2 Runtime 在较长用户数据根目录下
        // 创建 controller 时触发内部路径限制。摘要仍足以在单进程内隔离。
        options.ProfileName = $"win-{hash[..16]}";
        return options;
    }

    /// <summary>串行化 WebView2 Controller 初始化，避免多个可见窗口同时进入原生初始化。</summary>
    internal WebView2InitializationCoordinator InitializationCoordinator => _initialization;

    /// <summary>释放环境；仅删除由本实例创建的临时用户数据目录。</summary>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _initialization.Dispose();
        _environment = null;
        if (_userDataRoot is { } root)
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return ValueTask.CompletedTask;
    }
}
