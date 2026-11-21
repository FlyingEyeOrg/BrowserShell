using System.Globalization;
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
    private bool _initialized;
    private bool _disposed;

    private WebViewWindowEnvironment(WebView2InitializationCoordinator initialization)
    {
        _initialization = initialization;
    }

    /// <summary>底层 WebView2 环境。初始化前访问会抛出异常。</summary>
    public CoreWebView2Environment Core =>
        _environment ?? throw new InvalidOperationException("WebView2 环境尚未初始化，请先调用 InitializeAsync。");

    private CoreWebView2Environment? _environment { get; set; }

    /// <summary>
    /// 创建环境。
    /// </summary>
    /// <param name="dispatcher">承载窗口的 WPF Dispatcher。</param>
    /// <param name="browserExecutableFolder">
    /// 固定的 WebView2 Runtime 目录。为 null 时使用系统安装的 Evergreen Runtime。
    /// </param>
    /// <param name="userDataFolder">
    /// 自定义用户数据目录。为 null 时使用进程级临时目录；传入的目录不会在释放时删除。
    /// </param>
    /// <remarks>
    /// <para><b>默认目录跨进程复用，而不是每次进程启动都换新目录。</b>早期实现把随机 GUID 拼进路径，
    /// 每个进程都得到一个全新的 WebView2 用户数据根目录。实测（WebView2 Runtime 157）：
    /// 全新根目录下 <c>EnsureCoreWebView2Async</c> 约有 <b>50%</b> 概率空转 <b>5.2 秒</b>
    /// （期间 CPU 与内存完全不变，属 Runtime 内部的等待），而复用根目录、仅新建 profile 时
    /// 稳定在 250~290 毫秒。门户每次开窗都是新进程，因此每次都有约一半概率慢 5 秒，
    /// 且被误认为"WebView2 环境初始化慢"。</para>
    ///
    /// <para>现在改用固定根目录 <c>%TEMP%/BrowserShell/&lt;exe 名&gt;-&lt;用户 SID 摘要&gt;</c>：
    /// 同一应用的不同进程复用同一目录（消除 5 秒空转），不同应用之间仍相互隔离。
    /// 窗口级隔离依旧由 <see cref="CreateControllerOptions"/> 的独立 ProfileName 保证，
    /// Cookie 与缓存不会互相串用。</para>
    ///
    /// <para>目录不再随进程退出删除——复用正是收益来源。WebView2 自身会在其中维护缓存，
    /// 磁盘占用由 Runtime 自行回收；如需完全干净的环境，调用方显式传 <paramref name="userDataFolder"/> 即可。</para>
    /// </remarks>
    public static WebViewWindowEnvironment Create(
        Dispatcher dispatcher,
        string? browserExecutableFolder = null,
        string? userDataFolder = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        var root = userDataFolder ?? GetDefaultUserDataFolder();
        Directory.CreateDirectory(root);
        var environment = new WebViewWindowEnvironment(
            new WebView2InitializationCoordinator(dispatcher))
        {
            BrowserExecutableFolder = browserExecutableFolder,
            UserDataFolder = root,
        };
        return environment;
    }

    /// <summary>
    /// 计算默认用户数据目录：<c>%TEMP%/BrowserShell/&lt;exe 名&gt;-&lt;用户标识摘要&gt;</c>。
    /// </summary>
    /// <remarks>
    /// <para>键里包含可执行文件名与当前用户标识的摘要，因此：同一应用跨进程复用（消除 WebView2
    /// 在全新根目录下的 5 秒空转）；不同应用、不同用户之间互不干扰。</para>
    /// <para>不使用 PID：PID 每次启动都变，等于每次都是新目录，正是要避免的情形。</para>
    /// </remarks>
    private static string GetDefaultUserDataFolder()
    {
        var processName = "app";
        try
        {
            // 取 exe 名（不含扩展名）。单文件发布或重命名后仍能区分不同应用。
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
            {
                processName = Path.GetFileNameWithoutExtension(path);
            }
        }
        catch (Exception)
        {
            // 取不到就退回通用名，不影响可用性。
        }

        // 用用户标识的摘要参与键，避免多用户在同一台机器上共用缓存目录。
        var identity = Environment.UserName;
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..12];

        var name = $"{SanitizeFolderName(processName)}-{hash}";
        return Path.Combine(Path.GetTempPath(), "BrowserShell", name);
    }

    /// <summary>把任意字符串中的非法文件名字符替换为下划线，并限制长度。</summary>
    private static string SanitizeFolderName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        }

        var cleaned = builder.ToString().Trim();
        if (cleaned.Length == 0) cleaned = "app";
        return cleaned.Length <= 40 ? cleaned : cleaned[..40];
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

    /// <summary>
    /// 释放环境。不再删除默认用户数据目录——该目录跨进程复用，
    /// 删掉它会让下一次启动重新经历 WebView2 的 5 秒空转（见 <see cref="Create"/>）。
    /// </summary>
    /// <remarks>
    /// 由调用方显式传入 <c>userDataFolder</c> 时，本来就约定"不会删除调用方的目录"。
    /// 因此两种情形都不做删除。缓存由 WebView2 Runtime 自行回收。
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _initialization.Dispose();
        _environment = null;
        return ValueTask.CompletedTask;
    }
}
