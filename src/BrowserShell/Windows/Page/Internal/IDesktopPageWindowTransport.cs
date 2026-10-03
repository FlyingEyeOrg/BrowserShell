using BrowserShell;

namespace BrowserShell;

/// <summary>隔离窗口业务状态与 SignalR 传输实现。</summary>
internal interface IPageWindowTransport
{
    Task UpsertAsync(AgentWindow window);

    Task ActivateAsync(string windowId);

    Task CloseAsync(string windowId);
}
