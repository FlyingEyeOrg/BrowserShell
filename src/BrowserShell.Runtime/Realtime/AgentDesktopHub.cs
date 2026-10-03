using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using BrowserShell;

namespace BrowserShell.Runtime;

/// <summary>接入服务与 BrowserShell 之间唯一的双向实时通道。</summary>
internal sealed partial class AgentDesktopHub : Hub
{
    private readonly AgentAuthorizationStore _authorization;
    private readonly AgentClientSessionRegistry _sessions;
    private readonly WindowSnapshotAssembler _snapshots;
    private readonly WindowManager _windows;
    private readonly BrowserWindowManager _browserWindows;
    private readonly RuntimeSettings _settings;
    private readonly ILogger<AgentDesktopHub> _logger;

    public AgentDesktopHub(AgentAuthorizationStore authorization, AgentClientSessionRegistry sessions,
        WindowSnapshotAssembler snapshots,
        WindowManager windows,
        BrowserWindowManager browserWindows,
        RuntimeSettings settings,
        ILogger<AgentDesktopHub> logger)
    {
        _authorization = authorization;
        _sessions = sessions;
        _snapshots = snapshots;
        _windows = windows;
        _browserWindows = browserWindows;
        _settings = settings;
        _logger = logger;
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var session = _sessions.Disconnect(Context.ConnectionId);
        if (session is not null)
        {
            await _windows.HideServiceAsync(session.Registration.ServiceInstanceId);
            await _browserWindows.HideServiceAsync(session.Registration.ServiceInstanceId);
        }
        await base.OnDisconnectedAsync(exception);
    }

    public async Task<AgentClientRegistrationResult> RegisterClientAsync(AgentClientRegistration registration)
    {
        RequireAccess(registration.ServiceInstanceId);
        ValidateRegistration(registration);
        var session = _sessions.Replace(Context.ConnectionId, registration);
        await _windows.HideServiceAsync(registration.ServiceInstanceId);
        // 注册只建立页面管理器，不等待 WebView2 实例预热。服务完成快照同步后即可启动业务，
        // 页面实例由全局后台队列补齐，预热失败不得使 SignalR 注册和服务进程失败。
        await _windows.WarmAsync(
            registration.ServiceInstanceId,
            registration,
            Context.ConnectionAborted,
            prewarm: false);
        _ = PrewarmSafelyAsync(registration);
        var views = registration.Views.Select(view => new AgentViewRegistrationResult(
            view.Name, view.Version,
            AgentViewRegistrationState.Deferred,
            0)).ToArray();
        session.BeginSynchronization();
        return new AgentClientRegistrationResult(registration.ServiceInstanceId, session.LeaseId,
            _settings.HeartbeatIntervalSeconds, _settings.LeaseTimeoutSeconds,
            _settings.ReconnectGracePeriodSeconds, views);
    }

    private async Task PrewarmSafelyAsync(AgentClientRegistration registration)
    {
        try
        {
            await _windows.EnsureWarmAsync(
                registration.ServiceInstanceId,
                registration,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogPrewarmFailed(_logger, registration.ServiceInstanceId, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "服务 {ServiceInstanceId} 的 BrowserShell 页面后台预热失败。")]
    private static partial void LogPrewarmFailed(ILogger logger, string serviceInstanceId, Exception exception);

    public async Task SynchronizeAsync(IAsyncEnumerable<WindowSnapshotFrame> frames)
    {
        var session = RequireConnectionSession();
        var snapshot = await _snapshots.AssembleAsync(frames, session.LeaseId, Context.ConnectionAborted);
        var browserWindows = _browserWindows.GetDesiredWindows(session.Registration.ServiceInstanceId);
        WindowOwnershipGraphValidator.Validate(
            snapshot.Windows,
            browserWindows);
        await _windows.ReconcileAsync(session.Registration.ServiceInstanceId, session.Registration,
            snapshot.Windows, Context.ConnectionAborted);
        await _browserWindows.BindSynchronizedOwnersAsync(
            session.Registration.ServiceInstanceId,
            Context.ConnectionAborted);
        session.CompleteSynchronization();
    }

    public async Task UpsertWindowAsync(string serviceInstanceId, SessionCommand<AgentWindow> command)
    {
        var session = RequireCommand(serviceInstanceId, command.LeaseId, command.Sequence);
        try
        {
            var pageWindows = _windows.GetDesiredWindows(serviceInstanceId)
                .Where(window => !string.Equals(window.WindowId, command.Payload.WindowId, StringComparison.Ordinal))
                .ToList();
            if (string.Equals(command.Payload.Status, "pending", StringComparison.Ordinal))
                pageWindows.Add(command.Payload);
            WindowOwnershipGraphValidator.Validate(
                pageWindows,
                _browserWindows.GetDesiredWindows(serviceInstanceId));
            await _windows.UpsertAsync(
                serviceInstanceId,
                session.Registration,
                command.Payload,
                Context.ConnectionAborted);
        }
        catch (InvalidOperationException exception)
        {
            Serilog.Log.Error(exception,
                "BrowserShellPageWindowUpsertFailed for {ServiceInstanceId} {WindowId}",
                serviceInstanceId,
                command.Payload.WindowId);
            throw new HubException(exception.Message);
        }
    }

    public Task ActivateWindowAsync(string serviceInstanceId, SessionCommand<WindowReference> command)
    {
        RequireCommand(serviceInstanceId, command.LeaseId, command.Sequence);
        return _windows.ActivateAsync(serviceInstanceId, command.Payload.WindowId);
    }

    public Task CloseWindowAsync(string serviceInstanceId, SessionCommand<WindowReference> command)
    {
        RequireCommand(serviceInstanceId, command.LeaseId, command.Sequence);
        return _windows.CloseFromServiceAsync(serviceInstanceId, command.Payload.WindowId);
    }

    public LeaseHeartbeatResult HeartbeatAsync(string serviceInstanceId, LeaseHeartbeat heartbeat)
    {
        var session = _sessions.Require(serviceInstanceId, Context.ConnectionId, heartbeat.LeaseId);
        session.Heartbeat();
        return new LeaseHeartbeatResult(session.LeaseId, true,
            DateTimeOffset.UtcNow.AddSeconds(_settings.LeaseTimeoutSeconds));
    }

    private AgentClientSession RequireCommand(string serviceInstanceId, string leaseId, long sequence)
    {
        var session = _sessions.Require(serviceInstanceId, Context.ConnectionId, leaseId);
        if (session.State != AgentSessionState.Online || !session.TryAcceptSequence(sequence))
            throw new HubException("增量命令必须在 Online 会话中按严格递增序号发送。");
        return session;
    }

    private AgentClientSession RequireConnectionSession()
    {
        var session = _sessions.Snapshot().SingleOrDefault(item =>
            string.Equals(item.ConnectionId, Context.ConnectionId, StringComparison.Ordinal));
        return session ?? throw new HubException("当前连接尚未注册服务会话。");
    }

    private void RequireAccess(string serviceInstanceId)
    {
        var auth = AgentHost.ReadAuthContext(Context.User!);
        if (auth is null || !_authorization.IsAuthorized(auth, serviceInstanceId))
            throw new HubException("当前客户端无权访问此服务实例。");
    }

    private void ValidateRegistration(AgentClientRegistration registration)
    {
        if (!IsAllowedServiceEndpoint(registration.Endpoint))
            throw new HubException("服务页面端点必须是本机 HTTP(S) 地址。");
        if (registration.WindowPolicy.MaxVisibleWindowCount is <= 0
            || registration.WindowPolicy.MaxVisibleWindowCount > _settings.WindowResources.MaxServiceVisibleWindowCount)
            throw new HubException("服务可见窗口数超出 Agent 限制。");
        if (!IsValidIconPath(registration.IconPath))
            throw new HubException("服务通用图标路径无效。");
        if (registration.Views.Count > _settings.WindowResources.MaxViewsPerSession
            || registration.Views.GroupBy(item => item.Name, StringComparer.Ordinal).Any(group => group.Count() > 1)
            || registration.Views.Any(item => item.PrewarmWindowCount < 0
                || item.PrewarmWindowCount > _settings.WindowResources.MaxPrewarmWindowCountPerView
                || !IsValidIconPath(item.IconPath)))
            throw new HubException("服务页面定义无效或超出 Agent 限制。");
    }

    public Task SynchronizeBrowserWindowsAsync(
        string serviceInstanceId,
        string leaseId,
        IReadOnlyList<AgentBrowserWindow> windows)
    {
        _sessions.Require(serviceInstanceId, Context.ConnectionId, leaseId);
        return _browserWindows.ReconcileAsync(serviceInstanceId, windows, Context.ConnectionAborted);
    }

    public Task UpsertBrowserWindowAsync(
        string serviceInstanceId,
        SessionCommand<AgentBrowserWindow> command)
    {
        RequireCommand(serviceInstanceId, command.LeaseId, command.Sequence);
        var browserWindows = _browserWindows.GetDesiredWindows(serviceInstanceId)
            .Where(window => !string.Equals(window.WindowId, command.Payload.WindowId, StringComparison.Ordinal))
            .Append(command.Payload)
            .ToArray();
        WindowOwnershipGraphValidator.Validate(
            _windows.GetDesiredWindows(serviceInstanceId),
            browserWindows);
        return _browserWindows.UpsertAsync(serviceInstanceId, command.Payload, Context.ConnectionAborted);
    }

    public Task ExecuteBrowserWindowCommandAsync(
        string serviceInstanceId,
        SessionCommand<BrowserWindowCommand> command)
    {
        RequireCommand(serviceInstanceId, command.LeaseId, command.Sequence);
        return _browserWindows.ExecuteAsync(serviceInstanceId, command.Payload);
    }

    private static bool IsValidIconPath(string? path)
    {
        if (path is null) return true;
        if (string.IsNullOrWhiteSpace(path)
            || !path.StartsWith('/')
            || path.StartsWith("//", StringComparison.Ordinal)
            || path.Contains('?')
            || path.Contains('#'))
        {
            return false;
        }

        var extension = System.IO.Path.GetExtension(path);
        return extension.Equals(".ico", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".png", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsAllowedServiceEndpoint(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https")
            || endpoint.Host.Trim('[', ']') is not ("127.0.0.1" or "::1")
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Fragment)
            || !string.IsNullOrEmpty(endpoint.Query)
            || endpoint.AbsolutePath != "/")
        {
            return false;
        }

        var authorityStart = value.IndexOf("://", StringComparison.Ordinal) + 3;
        var authorityLength = value.AsSpan(authorityStart).IndexOfAny('/', '?', '#');
        var authority = authorityLength < 0
            ? value.AsSpan(authorityStart)
            : value.AsSpan(authorityStart, authorityLength);
        var separator = authority.LastIndexOf(':');
        return separator >= 0
            && int.TryParse(authority[(separator + 1)..], out var port)
            && port is > 0 and <= 65535;
    }
}
