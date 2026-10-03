namespace BrowserShell.Service.SDK;

/// <summary>BrowserShell 通过当前 SignalR 连接调用接入服务的方法。</summary>
internal interface IBrowserShellClient
{
    Task<WindowResultAck> CompleteWindowAsync(WindowResultSubmission submission);

    Task<WindowActionAck> InvokeWindowActionAsync(WindowActionSubmission submission);

    Task<WindowPresentedAck> NotifyWindowPresentedAsync(WindowPresentedNotification notification);

    Task<AgentWindowStateSnapshot?> GetWindowStateAsync(WindowStateQuery query);

    Task<BrowserWindowClosingAck> RequestBrowserWindowCloseAsync(BrowserWindowClosingRequest request);

    Task NotifyBrowserWindowClosedAsync(BrowserWindowClosedNotification notification);

    Task InvalidateSessionAsync(SessionInvalidation invalidation);
}
