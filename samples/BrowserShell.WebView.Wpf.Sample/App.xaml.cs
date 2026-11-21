using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace BrowserShell.WebView.Wpf.Sample;

/// <summary>
/// 示例程序：演示如何用 ASP.NET Core 风格的依赖注入接入 BrowserShell。
/// </summary>
/// <remarks>
/// <para>注册只需要一行 <c>AddBrowserShellWebView()</c>；之后通过
/// <see cref="IWebViewShellFactory"/>（或直接注入 <see cref="WebViewShell"/>）拿到外壳。</para>
/// <para>这里刻意显式设置 <see cref="WebViewShellOptions.ApplicationFolderName"/>：
/// 默认按 exe 文件名命名时，一旦重命名可执行文件就会指向全新目录，缓存与登录态随之"丢失"。</para>
/// </remarks>
public partial class App : Application
{
    private static IServiceProvider? _services;

    /// <summary>
    /// 进程级服务容器。
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// 在 <see cref="OnStartup"/> 之前访问——那属于调用方的时序错误，应尽早失败。
    /// </exception>
    internal static IServiceProvider Services =>
        _services ?? throw new InvalidOperationException(
            $"{nameof(App)}.{nameof(Services)} 尚未初始化，请在 OnStartup 之后访问。");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 示例程序：把未处理异常显示出来，避免窗口未出现时没有线索。
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ShowFatal(args.ExceptionObject as Exception);

        var services = new ServiceCollection();
        // 把 UI 线程的 Dispatcher 交给容器，供 WebViewShell 使用。
        services.AddSingleton(Dispatcher);
        services.AddBrowserShellWebView(options =>
        {
            options.ApplicationFolderName = "BrowserShell.Sample";
        });

        _services = services.BuildServiceProvider();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 释放容器会连带释放 WebViewShell（注册为 IAsyncDisposable 单例）。
        switch (_services)
        {
            case IAsyncDisposable asyncDisposable:
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }

        _services = null;
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowFatal(e.Exception);
        // 已提示用户，保持进程存活以便观察窗口状态。
        e.Handled = true;
    }

    private static void ShowFatal(Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        MessageBox.Show(
            $"{exception.GetType().Name}: {exception.Message}\n\n{exception.StackTrace}",
            "BrowserShell 示例 — 未处理异常",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
