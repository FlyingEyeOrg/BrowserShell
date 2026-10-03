namespace BrowserShell;

/// <summary>宿主向 BrowserShell 会话发送 BrowserWindow 投影和命令的适配边界。</summary>
internal interface IBrowserWindowTransport
{
    Task UpsertBrowserWindowAsync(AgentBrowserWindow window, CancellationToken token);

    Task ExecuteBrowserWindowCommandAsync(BrowserWindowCommand command, CancellationToken token);
}
