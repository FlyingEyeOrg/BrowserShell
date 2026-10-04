using System.Windows;
using System.Windows.Threading;

namespace BrowserShell.WebView.Wpf.Sample;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 示例程序：把未处理异常显示出来，避免窗口未出现时没有线索。
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ShowFatal(args.ExceptionObject as Exception);
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
