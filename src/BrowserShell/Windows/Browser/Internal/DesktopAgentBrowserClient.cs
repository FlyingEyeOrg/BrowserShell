using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace BrowserShell;

/// <summary>供 Host 等仅使用 BrowserWindow 的平台进程复用的 Agent 会话。</summary>
internal sealed partial class BrowserShellBrowserClient : IBrowserWindowTransport, IAsyncDisposable
{
    private static readonly byte[] EmptyWindows = JsonSerializer.SerializeToUtf8Bytes(Array.Empty<AgentWindow>());
    private readonly ILogger<BrowserShellBrowserClient> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly BrowserWindowManager _windows;
    private HttpClient _http = CreateHttpClient();
    private HubConnection? _connection;
    private AgentClientRegistration? _registration;
    private AgentClientRegistrationResult? _session;
    private CancellationTokenSource? _lifetime;
    private string? _clientId;
    private string? _clientSecret;
    private AgentAccessToken? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;
    private long _sequence;

    public BrowserShellBrowserClient(ILogger<BrowserShellBrowserClient> logger)
    {
        _logger = logger;
        _windows = new BrowserWindowManager(this);
    }

    public IBrowserWindowService BrowserWindows => _windows;

    public async Task StartAsync(
        Uri agentEndpoint,
        string clientId,
        string clientSecret,
        string instanceId,
        Uri applicationEndpoint,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (_connection is not null) throw new InvalidOperationException("BrowserShell Browser 客户端已启动。");
        _http.BaseAddress = agentEndpoint;
        _clientId = clientId;
        _clientSecret = clientSecret;
        _registration = new AgentClientRegistration(
            instanceId,
            applicationEndpoint.GetLeftPart(UriPartial.Authority) + "/",
            DateTimeOffset.UtcNow,
            new AgentCallbackCredentials("browser-only", "browser-only"),
            new AgentWindowPolicy(16),
            []);
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _connection = new HubConnectionBuilder()
            .WithUrl(new Uri(agentEndpoint, "/api/v1/hubs/clients"), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.AccessTokenProvider = GetAccessTokenAsync;
                options.WebSocketConfiguration = socket => socket.Proxy = null;
                options.HttpMessageHandlerFactory = handler =>
                {
                    if (handler is HttpClientHandler http) http.UseProxy = false;
                    return handler;
                };
            })
            .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)])
            .Build();
        RegisterCallbacks(_connection);
        _connection.Reconnected += _ => RecoverSafelyAsync();
        _connection.Closed += exception =>
        {
            if (_lifetime is { IsCancellationRequested: false }) _ = ReconnectAsync(_lifetime.Token);
            return Task.CompletedTask;
        };
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startup.CancelAfter(timeout);
        await _connection.StartAsync(startup.Token).ConfigureAwait(false);
        await RecoverAsync(startup.Token).ConfigureAwait(false);
        _ = HeartbeatAsync(_lifetime.Token);
    }

    public Task UpsertBrowserWindowAsync(AgentBrowserWindow window, CancellationToken token) =>
        SendAsync(BrowserShellHubMethods.UpsertBrowserWindowAsync, window, token);

    public Task ExecuteBrowserWindowCommandAsync(BrowserWindowCommand command, CancellationToken token) =>
        SendAsync(BrowserShellHubMethods.ExecuteBrowserWindowCommandAsync, command, token);

    private void RegisterCallbacks(HubConnection connection)
    {
        connection.On<BrowserWindowClosingRequest, BrowserWindowClosingAck>(
            nameof(IAgentDesktopClient.RequestBrowserWindowCloseAsync),
            value => _windows.RequestCloseAsync(value, CancellationToken.None));
        connection.On<BrowserWindowClosedNotification>(
            nameof(IAgentDesktopClient.NotifyBrowserWindowClosedAsync),
            value => _windows.NotifyClosedAsync(value, CancellationToken.None));
    }

    private async Task RecoverAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { await RecoverCoreAsync(token).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private async Task RecoverCoreAsync(CancellationToken token)
    {
        var connection = _connection ?? throw new InvalidOperationException("BrowserShell 连接尚未创建。");
        var registration = _registration ?? throw new InvalidOperationException("BrowserShell 注册信息尚未配置。");
        _session = await connection.InvokeAsync<AgentClientRegistrationResult>(
            BrowserShellHubMethods.RegisterClientAsync, registration, token).ConfigureAwait(false);
        Interlocked.Exchange(ref _sequence, 0);
        await connection.InvokeAsync(
            BrowserShellHubMethods.SynchronizeBrowserWindowsAsync,
            registration.ServiceInstanceId,
            _session.LeaseId,
            await _windows.SnapshotAsync(token).ConfigureAwait(false),
            token).ConfigureAwait(false);
        await connection.InvokeCoreAsync(
            BrowserShellHubMethods.SynchronizeAsync,
            [EmptySnapshot(_session.LeaseId)],
            token).ConfigureAwait(false);
    }

    private async Task SendAsync<T>(string method, T payload, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_connection?.State != HubConnectionState.Connected || _session is null || _registration is null)
                throw new InvalidOperationException("BrowserShell Browser 会话尚未就绪。");
            var command = new SessionCommand<T>(_session.LeaseId, Interlocked.Increment(ref _sequence), payload);
            await _connection.InvokeCoreAsync(
                method,
                [_registration.ServiceInstanceId, command],
                token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task<string?> GetAccessTokenAsync()
    {
        if (_accessToken is not null && _accessTokenExpiresAt - DateTimeOffset.UtcNow > TimeSpan.FromMinutes(1))
            return _accessToken.AccessToken;
        using var response = await _http.PostAsync(
            "api/v1/auth/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _clientId!,
                ["client_secret"] = _clientSecret!,
            })).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        _accessToken = await response.Content.ReadFromJsonAsync<AgentAccessToken>().ConfigureAwait(false)
            ?? throw new InvalidDataException("BrowserShell 未返回访问令牌。");
        _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(_accessToken.ExpiresIn);
        return _accessToken.AccessToken;
    }

    private async Task HeartbeatAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var session = _session;
                if (session is not null && _connection?.State == HubConnectionState.Connected)
                    await _connection.InvokeAsync<LeaseHeartbeatResult>(
                        BrowserShellHubMethods.HeartbeatAsync,
                        _registration!.ServiceInstanceId,
                        new LeaseHeartbeat(session.LeaseId),
                        token).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, session?.HeartbeatIntervalSeconds ?? 5)), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                LogConnectionFailed(_logger, exception);
                await RecoverSafelyAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task ReconnectAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_connection?.State == HubConnectionState.Disconnected)
                    await _connection.StartAsync(token).ConfigureAwait(false);
                await RecoverAsync(token).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                LogConnectionFailed(_logger, exception);
                await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            }
        }
    }

    private async Task RecoverSafelyAsync()
    {
        try { await RecoverAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception exception) { LogConnectionFailed(_logger, exception); }
    }

    private static async IAsyncEnumerable<WindowSnapshotFrame> EmptySnapshot(string leaseId)
    {
        var snapshotId = Guid.NewGuid().ToString("N");
        yield return new WindowSnapshotFrame(
            WindowSnapshotFrameKind.Header,
            snapshotId,
            leaseId,
            1,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            0,
            EmptyWindows.LongLength);
        yield return new WindowSnapshotFrame(WindowSnapshotFrameKind.Chunk, snapshotId, Index: 0, Payload: EmptyWindows);
        yield return new WindowSnapshotFrame(
            WindowSnapshotFrameKind.Trailer,
            snapshotId,
            ActualWindowCount: 0,
            ActualContentBytes: EmptyWindows.LongLength,
            ContentHash: Convert.ToHexString(SHA256.HashData(EmptyWindows)).ToLowerInvariant());
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime?.Cancel();
        if (_connection is not null) await _connection.DisposeAsync().ConfigureAwait(false);
        _lifetime?.Dispose();
        _http.Dispose();
        _gate.Dispose();
    }

    private static HttpClient CreateHttpClient() => new(new HttpClientHandler { UseProxy = false });

    [LoggerMessage(Level = LogLevel.Warning, Message = "BrowserShell Browser 客户端连接或恢复失败。")]
    private static partial void LogConnectionFailed(ILogger logger, Exception exception);
}
