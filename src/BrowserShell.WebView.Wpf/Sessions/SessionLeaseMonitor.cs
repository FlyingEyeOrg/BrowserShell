using Microsoft.Extensions.Hosting;

namespace BrowserShell.WebView.Wpf;

/// <summary>回收心跳超时的易失服务会话及其窗口资源。</summary>
internal sealed class SessionLeaseMonitor : BackgroundService
{
    private readonly AgentClientSessionRegistry _sessions;
    private readonly WindowManager _windows;
    private readonly BrowserWindowManager _browserWindows;
    private readonly RuntimeSettings _settings;

    public SessionLeaseMonitor(
        AgentClientSessionRegistry sessions,
        WindowManager windows,
        BrowserWindowManager browserWindows,
        RuntimeSettings settings)
    {
        _sessions = sessions;
        _windows = windows;
        _browserWindows = browserWindows;
        _settings = settings;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _settings.HeartbeatIntervalSeconds)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var session in _sessions.Snapshot())
            {
                var timeout = session.State == AgentSessionState.Disconnected
                    ? TimeSpan.FromSeconds(_settings.ReconnectGracePeriodSeconds)
                    : TimeSpan.FromSeconds(_settings.LeaseTimeoutSeconds);
                if (!session.IsLeaseExpired(timeout)
                    || !_sessions.Expire(session.Registration.ServiceInstanceId, session.LeaseId)) continue;
                await _windows.CloseServiceAsync(session.Registration.ServiceInstanceId);
                await _browserWindows.CloseServiceAsync(session.Registration.ServiceInstanceId);
            }
        }
    }
}
