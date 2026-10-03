using Microsoft.AspNetCore.SignalR;
using BrowserShell.Service.SDK;

namespace BrowserShell.WebView.Wpf;

/// <summary>通过当前租约的唯一 SignalR 连接向权威服务提交即时交互。</summary>
internal sealed class AgentInteractionClient
{
    private readonly IHubContext<BrowserShellHub> _hub;
    private readonly AgentClientSessionRegistry _sessions;
    private readonly RuntimeSettings _settings;

    public AgentInteractionClient(IHubContext<BrowserShellHub> hub,
        AgentClientSessionRegistry sessions, RuntimeSettings settings)
    {
        _hub = hub;
        _sessions = sessions;
        _settings = settings;
    }

    public async Task<WindowResultAck> CompleteWindowAsync(
        string serviceInstanceId, WindowResultSubmission submission, CancellationToken token)
    {
        if (!_sessions.TryGet(serviceInstanceId, out var session)
            || session.State != AgentSessionState.Online)
            throw new InvalidOperationException("接入服务当前没有可用的在线会话。");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, session.LeaseCancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.Callback.ResponseTimeoutSeconds)));
        return await _hub.Clients.Client(session.ConnectionId)
            .InvokeCoreAsync<WindowResultAck>(
                nameof(IBrowserShellClient.CompleteWindowAsync),
                [submission],
                timeout.Token)
            .WaitAsync(timeout.Token);
    }

    public async Task<WindowActionAck> InvokeWindowActionAsync(
        string serviceInstanceId,
        WindowActionSubmission submission,
        CancellationToken token)
    {
        if (!_sessions.TryGet(serviceInstanceId, out var session)
            || session.State != AgentSessionState.Online)
            throw new InvalidOperationException("接入服务当前没有可用的在线会话。");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, session.LeaseCancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.Callback.ResponseTimeoutSeconds)));
        return await _hub.Clients.Client(session.ConnectionId)
            .InvokeCoreAsync<WindowActionAck>(
                nameof(IBrowserShellClient.InvokeWindowActionAsync),
                [submission],
                timeout.Token)
            .WaitAsync(timeout.Token);
    }

    public async Task<AgentWindowStateSnapshot?> GetWindowStateAsync(
        string serviceInstanceId, string windowId, CancellationToken token)
    {
        if (!_sessions.TryGet(serviceInstanceId, out var session)
            || session.State != AgentSessionState.Online) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, session.LeaseCancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.Callback.ReconcileTimeoutSeconds)));
        return await _hub.Clients.Client(session.ConnectionId)
            .InvokeCoreAsync<AgentWindowStateSnapshot?>(
                nameof(IBrowserShellClient.GetWindowStateAsync),
                [new WindowStateQuery(windowId)],
                timeout.Token)
            .WaitAsync(timeout.Token);
    }

    public async Task<WindowPresentedAck> NotifyWindowPresentedAsync(
        string serviceInstanceId,
        WindowPresentedNotification notification,
        CancellationToken token)
    {
        if (!_sessions.TryGet(serviceInstanceId, out var session)
            || session.State != AgentSessionState.Online)
            throw new InvalidOperationException("接入服务当前没有可用的在线会话。");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, session.LeaseCancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.Callback.ResponseTimeoutSeconds)));
        return await _hub.Clients.Client(session.ConnectionId)
            .InvokeCoreAsync<WindowPresentedAck>(
                nameof(IBrowserShellClient.NotifyWindowPresentedAsync),
                [notification],
                timeout.Token)
            .WaitAsync(timeout.Token);
    }

    public async Task<BrowserWindowClosingAck> RequestBrowserWindowCloseAsync(
        string serviceInstanceId,
        BrowserWindowClosingRequest request,
        CancellationToken token)
    {
        var session = RequireOnlineSession(serviceInstanceId);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, session.LeaseCancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.Callback.ResponseTimeoutSeconds)));
        return await _hub.Clients.Client(session.ConnectionId)
            .InvokeCoreAsync<BrowserWindowClosingAck>(
                nameof(IBrowserShellClient.RequestBrowserWindowCloseAsync),
                [request],
                timeout.Token)
            .WaitAsync(timeout.Token);
    }

    public async Task NotifyBrowserWindowClosedAsync(
        string serviceInstanceId,
        BrowserWindowClosedNotification notification,
        CancellationToken token)
    {
        var session = RequireOnlineSession(serviceInstanceId);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, session.LeaseCancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.Callback.ResponseTimeoutSeconds)));
        await _hub.Clients.Client(session.ConnectionId)
            .SendAsync(nameof(IBrowserShellClient.NotifyBrowserWindowClosedAsync), notification, timeout.Token);
    }

    private AgentClientSession RequireOnlineSession(string serviceInstanceId)
    {
        if (!_sessions.TryGet(serviceInstanceId, out var session)
            || session.State != AgentSessionState.Online)
            throw new InvalidOperationException("接入服务当前没有可用的在线会话。");
        return session;
    }
}
