using System.Windows;

namespace BrowserShell.Runtime;

public partial class App : Application
{
    private AgentHost? _host;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            if (e.Args.Length > 1)
            {
                throw new ArgumentException("BrowserShell Runtime 启动时最多接受一个 JSON 配置文件路径。");
            }
            _host = await AgentHost.StartAsync(Dispatcher, e.Args.SingleOrDefault(), CancellationToken.None);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.WriteLine(exception);
            Console.Error.WriteLine(exception);
            Shutdown(1);
        }
    }
    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null) await _host.DisposeAsync();
        base.OnExit(e);
    }
}
