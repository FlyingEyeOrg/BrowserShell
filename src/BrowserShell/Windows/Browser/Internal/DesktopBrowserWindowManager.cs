using System.Collections.Concurrent;
using BrowserShell;

namespace BrowserShell;

/// <summary>保存当前 Web 程序进程期望的 BrowserWindow，并通过 Agent 投影呈现。</summary>
internal sealed class BrowserWindowManager :
    IBrowserWindowService,
    IBrowserWindowOperations
{
    private readonly IBrowserWindowTransport _client;
    private readonly ConcurrentDictionary<string, BrowserWindowEntry> _windows = new(StringComparer.Ordinal);
    private readonly WindowOwnershipRegistry _ownership;

    public BrowserWindowManager(
        IBrowserWindowTransport client,
        WindowOwnershipRegistry? ownership = null)
    {
        _client = client;
        _ownership = ownership ?? new WindowOwnershipRegistry();
    }

    public async Task<BrowserWindowHandle> OpenAsync(
        BrowserWindowOptions options,
        BrowserWindowLifecycle? lifecycle = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateUrl(options.Url, nameof(options));
        var origins = NormalizeOrigins(options.Url, options.AllowedOrigins);
        var titleBar = WindowTitleBarOptionsResolver.Resolve(null, options.TitleBar);
        var navigationUrl = await ResolveNavigationUriAsync(
            options.Url,
            options.BootstrapUriFactory,
            origins,
            cancellationToken);
        var window = new AgentBrowserWindow(
            Guid.NewGuid().ToString("N"),
            options.OwnerWindowId,
            navigationUrl.ToString(),
            options.Title,
            options.Width,
            options.Height,
            options.MinWidth,
            options.MinHeight,
            options.Center,
            options.Topmost,
            options.Focus,
            origins,
            1,
            titleBar,
            options.Url.ToString());
        _ownership.Register(
            "BrowserWindow",
            window.WindowId,
            window.OwnerWindowId,
            modal: false,
            closeToken => CloseSingleAsync(window.WindowId, closeToken));
        if (!_windows.TryAdd(window.WindowId, new BrowserWindowEntry(
                window,
                options.Url,
                options.BootstrapUriFactory,
                lifecycle)))
        {
            _ownership.Unregister(window.WindowId);
            throw new InvalidOperationException("BrowserWindow 标识冲突。");
        }

        try
        {
            await _client.UpsertBrowserWindowAsync(window, cancellationToken);
            return new BrowserWindowHandle(window.WindowId, this);
        }
        catch
        {
            _windows.TryRemove(window.WindowId, out _);
            _ownership.Unregister(window.WindowId);
            throw;
        }
    }

    public async Task ExecuteAsync(
        string windowId,
        BrowserWindowOperation operation,
        string? value,
        CancellationToken token)
    {
        if (!_windows.TryGetValue(windowId, out var entry))
            throw new InvalidOperationException($"BrowserWindow {windowId} 不存在。");

        if (operation == BrowserWindowOperation.LoadUrl)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var url))
                throw new ArgumentException("BrowserWindow URL 无效。", nameof(value));
            ValidateUrl(url, nameof(value));
            if (!entry.Window.AllowedOrigins.Contains(GetOrigin(url), StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("BrowserWindow 不能导航到未注册的 Origin。");
        }

        if (operation == BrowserWindowOperation.Close)
        {
            if (!await _ownership.CloseSubtreeAsync(windowId, token))
                await CloseSingleAsync(windowId, token);
            return;
        }

        await _client.ExecuteBrowserWindowCommandAsync(
            new BrowserWindowCommand(windowId, operation, value),
            token);

        if (operation is BrowserWindowOperation.LoadUrl or BrowserWindowOperation.SetTitle)
        {
            var updated = entry.Window with
            {
                Url = operation == BrowserWindowOperation.LoadUrl ? value! : entry.Window.Url,
                TargetUrl = operation == BrowserWindowOperation.LoadUrl ? value! : entry.Window.TargetUrl,
                Title = operation == BrowserWindowOperation.SetTitle ? value : entry.Window.Title,
                Revision = entry.Window.Revision + 1,
            };
            _windows.TryUpdate(windowId, entry with
            {
                Window = updated,
                TargetUrl = operation == BrowserWindowOperation.LoadUrl
                    ? new Uri(value!, UriKind.Absolute)
                    : entry.TargetUrl,
            }, entry);
        }
    }

    public async Task ReloadFromBootstrapAsync(string windowId, CancellationToken token)
    {
        if (!_windows.TryGetValue(windowId, out var entry))
            throw new InvalidOperationException($"BrowserWindow {windowId} 不存在。");

        var navigationUrl = await ResolveNavigationUriAsync(
            entry.TargetUrl,
            entry.BootstrapUriFactory,
            entry.Window.AllowedOrigins,
            token);
        await _client.ExecuteBrowserWindowCommandAsync(
            new BrowserWindowCommand(windowId, BrowserWindowOperation.LoadUrl, navigationUrl.ToString()),
            token);
        var updated = entry with
        {
            Window = entry.Window with
            {
                Url = navigationUrl.ToString(),
                Revision = entry.Window.Revision + 1,
            },
        };
        _windows.TryUpdate(windowId, updated, entry);
    }

    internal async Task<IReadOnlyList<AgentBrowserWindow>> SnapshotAsync(CancellationToken token)
    {
        var entries = _windows.Values.OrderBy(entry => entry.Window.WindowId, StringComparer.Ordinal).ToArray();
        var snapshot = new AgentBrowserWindow[entries.Length];
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var navigationUrl = await ResolveNavigationUriAsync(
                entry.TargetUrl,
                entry.BootstrapUriFactory,
                entry.Window.AllowedOrigins,
                token);
            snapshot[index] = entry.Window with { Url = navigationUrl.ToString() };
        }
        return snapshot;
    }

    internal async Task<BrowserWindowClosingAck> RequestCloseAsync(
        BrowserWindowClosingRequest request,
        CancellationToken token)
    {
        if (!_windows.TryGetValue(request.WindowId, out var entry))
            return new BrowserWindowClosingAck(request.RequestId, true);
        if (request.NotAfterUtc <= DateTimeOffset.UtcNow)
            return new BrowserWindowClosingAck(request.RequestId, false, "关闭请求已过期。");
        if (entry.Lifecycle?.ClosingAsync is null)
        {
            await _ownership.CloseDescendantsAsync(request.WindowId, token).ConfigureAwait(false);
            return new BrowserWindowClosingAck(request.RequestId, true);
        }

        if (!Enum.TryParse<BrowserWindowCloseSource>(request.Source, true, out var source))
            source = BrowserWindowCloseSource.WindowChrome;
        var decision = await entry.Lifecycle.ClosingAsync(
            new BrowserWindowClosingContext(
                request.WindowId,
                new Uri(request.Url),
                source),
            token);
        if (decision.Accepted)
            await _ownership.CloseDescendantsAsync(request.WindowId, token).ConfigureAwait(false);
        return new BrowserWindowClosingAck(
            request.RequestId,
            decision.Accepted,
            decision.Message,
            decision.Code);
    }

    internal Task NotifyClosedAsync(BrowserWindowClosedNotification notification, CancellationToken token) =>
        CompleteClosedAsync(notification.WindowId, token);

    private async Task CompleteClosedAsync(string windowId, CancellationToken token)
    {
        if (_windows.TryRemove(windowId, out var entry)
            && entry.Lifecycle?.ClosedAsync is not null)
            await entry.Lifecycle.ClosedAsync(windowId, token);
        _ownership.Unregister(windowId);
    }

    private async Task CloseSingleAsync(string windowId, CancellationToken token)
    {
        if (!_windows.TryRemove(windowId, out var entry)) return;
        _ownership.Unregister(windowId);
        if (entry.Lifecycle?.ClosedAsync is not null)
            await entry.Lifecycle.ClosedAsync(windowId, token).ConfigureAwait(false);
        await _client.ExecuteBrowserWindowCommandAsync(
            new BrowserWindowCommand(windowId, BrowserWindowOperation.Close),
            token).ConfigureAwait(false);
    }

    private static string[] NormalizeOrigins(Uri url, IReadOnlyList<string> origins)
    {
        IEnumerable<string> values = origins.Count == 0 ? [GetOrigin(url)] : origins;
        var normalized = values.Select(value =>
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var origin))
                throw new ArgumentException($"BrowserWindow Origin 无效：{value}", nameof(origins));
            ValidateUrl(origin, nameof(origins));
            return GetOrigin(origin);
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!normalized.Contains(GetOrigin(url), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("AllowedOrigins 必须包含启动 URL 的 Origin。", nameof(origins));
        return normalized;
    }

    private static string GetOrigin(Uri uri) => uri.GetLeftPart(UriPartial.Authority);

    private static void ValidateUrl(Uri url, string parameterName)
    {
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(url.UserInfo))
            throw new ArgumentException("BrowserWindow 只支持不包含用户信息的 HTTP(S) URL。", parameterName);
    }

    private static async ValueTask<Uri> ResolveNavigationUriAsync(
        Uri targetUrl,
        BrowserWindowBootstrapUriFactory? factory,
        IReadOnlyList<string> allowedOrigins,
        CancellationToken token)
    {
        var navigationUrl = factory is null
            ? targetUrl
            : await factory(targetUrl, token);
        if (navigationUrl is null)
            throw new InvalidOperationException("BrowserWindow 启动地址工厂返回了空地址。");
        ValidateUrl(navigationUrl, nameof(BrowserWindowOptions.BootstrapUriFactory));
        if (!allowedOrigins.Contains(GetOrigin(navigationUrl), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("BrowserWindow 启动地址不在允许的 Origin 中。");
        return navigationUrl;
    }

    private sealed record BrowserWindowEntry(
        AgentBrowserWindow Window,
        Uri TargetUrl,
        BrowserWindowBootstrapUriFactory? BootstrapUriFactory,
        BrowserWindowLifecycle? Lifecycle);
}
