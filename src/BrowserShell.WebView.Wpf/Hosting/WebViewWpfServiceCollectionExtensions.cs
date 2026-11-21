using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BrowserShell.WebView.Wpf;

/// <summary>
/// <see cref="IServiceCollection"/> 扩展：按 ASP.NET Core 惯例注册 BrowserShell 的服务。
/// </summary>
/// <remarks>
/// <para>典型用法（与 ASP.NET Core 的 <c>AddXxx</c> 一致）：</para>
/// <code>
/// // WPF 的 App.xaml.cs 或通用 Startup
/// var services = new ServiceCollection();
/// services.AddBrowserShellWebView(options =>
/// {
///     options.ApplicationFolderName = "SoftwareHub";
/// });
/// var provider = services.BuildServiceProvider();
/// </code>
///
/// <para>注册后的生命周期：</para>
/// <list type="bullet">
///   <item><description><see cref="WebViewShellOptions"/>：<c>Singleton</c>（经
///   <see cref="IOptions{TOptions}"/> 提供，支持 <c>IConfigureOptions</c> 管线）。</description></item>
///   <item><description><see cref="WebViewShell"/>：<c>Singleton</c>，且<b>延迟创建</b>——
///   首次解析时才初始化 WebView2 环境，避免容器构建阶段就去碰 UI 线程。</description></item>
/// </list>
///
/// <para><b>线程要求</b>：<see cref="WebViewShell"/> 必须在 UI 线程（创建窗口的
/// <see cref="Dispatcher"/>）上解析与使用。若在后台线程解析，请在 UI 线程调用
/// <see cref="IWebViewShellFactory.CreateAsync"/>。</para>
/// </remarks>
public static class WebViewWpfServiceCollectionExtensions
{
    /// <summary>注册 BrowserShell 的默认服务（<see cref="WebViewShell"/> 单例 + 配置）。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">可选的配置回调。</param>
    /// <remarks>
    /// 未调用本方法时，也可直接使用 <see cref="WebViewShell.CreateAsync(Dispatcher, WebViewShellOptions?, CancellationToken)"/>
    /// 的静态入口——两种方式等价，IOC 只是让依赖注入更顺手。
    /// </remarks>
    public static IServiceCollection AddBrowserShellWebView(
        this IServiceCollection services,
        Action<WebViewShellOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<IWebViewShellFactory, WebViewShellFactory>();
        services.TryAddSingleton(static provider =>
        {
            var factory = provider.GetRequiredService<IWebViewShellFactory>();
            // 同步等待：这里的异步链只做 WebView2 环境创建（实测毫秒级），
            // 且必须在 UI 线程上执行（EnsureCoreWebView2Async 的线程亲和性）。
            return factory.CreateAsync(CancellationToken.None).GetAwaiter().GetResult();
        });

        return services;
    }

    /// <summary>
    /// 注册 BrowserShell，并允许指定 <see cref="Dispatcher"/> 的来源（适用于 UI 线程不在
    /// 容器构建阶段可用的场景）。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="dispatcherAccessor">返回承载窗口的 Dispatcher。</param>
    /// <param name="configure">可选的配置回调。</param>
    public static IServiceCollection AddBrowserShellWebView(
        this IServiceCollection services,
        Func<IServiceProvider, Dispatcher> dispatcherAccessor,
        Action<WebViewShellOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dispatcherAccessor);

        services.AddSingleton(dispatcherAccessor);

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<IWebViewShellFactory, WebViewShellFactory>();
        services.TryAddSingleton(static provider =>
        {
            var factory = provider.GetRequiredService<IWebViewShellFactory>();
            return factory.CreateAsync(CancellationToken.None).GetAwaiter().GetResult();
        });

        return services;
    }
}

/// <summary>异步创建 <see cref="WebViewShell"/> 的工厂。</summary>
/// <remarks>
/// <see cref="WebViewShell"/> 的创建必须 <c>await</c>（要初始化 WebView2 环境），
/// 而 DI 的构造函数注入是同步的。需要精细控制时（例如在 UI 线程的
/// <c>Loaded</c> 事件里异步创建），注入本工厂而不是直接注入 <see cref="WebViewShell"/>。
/// </remarks>
public interface IWebViewShellFactory
{
    /// <summary>创建（或返回已创建的）外壳管理器。</summary>
    /// <param name="token">取消令牌。</param>
    /// <remarks>
    /// 实现为幂等：多次调用只初始化一次 WebView2 环境。
    /// </remarks>
    Task<WebViewShell> CreateAsync(CancellationToken token = default);
}

/// <summary>默认的 <see cref="IWebViewShellFactory"/> 实现。</summary>
internal sealed class WebViewShellFactory : IWebViewShellFactory, IAsyncDisposable
{
    private readonly IServiceProvider _provider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WebViewShell? _shell;
    private bool _disposed;

    public WebViewShellFactory(IServiceProvider provider) => _provider = provider;

    public async Task<WebViewShell> CreateAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_shell is { } existing)
        {
            return existing;
        }

        // 并发首个解析时只允许一次初始化：WebView2 环境本身不可重入。
        await _gate.WaitAsync(token);
        try
        {
            if (_shell is { } raced)
            {
                return raced;
            }

            var dispatcher = _provider.GetService<Dispatcher>()
                ?? Application.Current?.Dispatcher
                ?? Dispatcher.CurrentDispatcher;
            var options = _provider.GetService<IOptions<WebViewShellOptions>>()?.Value;
            _shell = await WebViewShell.CreateAsync(dispatcher, options, token);
            return _shell;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_shell is { } shell)
        {
            await shell.DisposeAsync();
            _shell = null;
        }

        _gate.Dispose();
    }
}
