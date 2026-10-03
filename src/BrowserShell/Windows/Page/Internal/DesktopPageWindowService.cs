using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using BrowserShell;

namespace BrowserShell;

internal sealed partial class PageWindowService : IPageWindowService, IWindowHandleOperations
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly WindowStore _store;
    private readonly IPageWindowEventSink _events;
    private readonly IPageWindowTransport? _notifier;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly ILogger<PageWindowService> _logger;
    private readonly WindowOwnershipRegistry _ownership;
    private readonly IViewCatalog _views;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<DesktopCompletion>> _waiters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _notificationTimers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IWindowLifecycleRegistration> _lifecycles = new(StringComparer.Ordinal);

    internal PageWindowService(
        WindowStore store,
        IPageWindowEventSink events,
        IViewCatalog views,
        IPageWindowTransport? notifier = null,
        IServiceScopeFactory? scopeFactory = null,
        ILogger<PageWindowService>? logger = null,
        WindowOwnershipRegistry? ownership = null)
    {
        _store = store;
        _events = events;
        _notifier = notifier;
        _scopeFactory = scopeFactory;
        _logger = logger ?? NullLogger<PageWindowService>.Instance;
        _ownership = ownership ?? new WindowOwnershipRegistry();
        _views = views;
    }

    public async Task<DialogResult<TResult>> ShowDialogAsync<TData, TResult>(
        string viewName,
        TData data,
        WindowOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var handle = await CreateAsync<TData, TResult>(viewName, data, true, options, null, cancellationToken);
        return await WaitForDialogAsync<TResult>(handle, cancellationToken);
    }

    public async Task<DialogResult<TResult>> ShowDialogAsync<TData, TResult>(
        string viewName,
        TData data,
        WindowOptions? options,
        WindowLifecycle<TResult> lifecycle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        var handle = await CreateAsync(viewName, data, true, options, lifecycle, cancellationToken);
        return await WaitForDialogAsync<TResult>(handle, cancellationToken);
    }

    private async Task<DialogResult<TResult>> WaitForDialogAsync<TResult>(
        WindowHandle handle,
        CancellationToken cancellationToken)
    {
        try
        {
            return await handle.WaitForCloseAsync<TResult>(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try
            {
                // ShowDialog 表示调用方拥有整个模态操作；等待被取消后不能留下无人消费的 Pending 窗口。
                using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await handle.CloseAsync(closeTimeout.Token);
            }
            catch (Exception exception)
            {
                LogCanceledDialogCloseFailed(_logger, exception, handle.WindowId);
            }
            throw;
        }
    }

    public Task<WindowHandle> ShowModalAsync<TData>(
        string viewName,
        TData data,
        WindowOptions? options = null,
        CancellationToken cancellationToken = default) =>
        CreateAsync<TData, JsonElement>(viewName, data, true, options, null, cancellationToken);

    public Task<WindowHandle> ShowModalAsync<TData, TResult>(
        string viewName,
        TData data,
        WindowOptions? options,
        WindowLifecycle<TResult> lifecycle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        return CreateAsync(viewName, data, true, options, lifecycle, cancellationToken);
    }

    public Task<WindowHandle> ShowAsync<TData>(
        string viewName,
        TData data,
        WindowOptions? options = null,
        CancellationToken cancellationToken = default) =>
        CreateAsync<TData, JsonElement>(viewName, data, false, options, null, cancellationToken);

    public Task<WindowHandle> ShowAsync<TData, TResult>(
        string viewName,
        TData data,
        WindowOptions? options,
        WindowLifecycle<TResult> lifecycle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        return CreateAsync(viewName, data, false, options, lifecycle, cancellationToken);
    }

    private async Task<WindowHandle> CreateAsync<TData, TResult>(
        string name,
        TData data,
        bool modal,
        WindowOptions? options,
        WindowLifecycle<TResult>? lifecycle,
        CancellationToken token)
    {
        var view = _views.GetRequired(name);
        options ??= new WindowOptions();
        if (!modal && options.OwnerWindowId is not null)
            throw new ArgumentException("非模态 PageWindow 不能指定 OwnerWindowId。", nameof(options));
        var dataJson = JsonSerializer.Serialize(data, JsonOptions);
        StandardDesktopPageValidator.ValidateRequest(name, dataJson);
        var titleBar = WindowTitleBarOptionsResolver.Resolve(view.TitleBar, options.TitleBar);
        var value = new StoredWindow(
            Guid.NewGuid().ToString("N"),
            name,
            view.Version,
            modal,
            options.Title,
            dataJson,
            options.Width,
            options.Height,
            options.Topmost,
            options.Focus,
            options.Flash,
            options.OwnerWindowId,
            "pending",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            TitleBar: titleBar);

        WindowLifecycleRegistration<TResult>? registration = null;
        if (lifecycle is not null)
        {
            var scopeFactory = _scopeFactory
                ?? throw new InvalidOperationException("Desktop 窗口生命周期需要可用的 IServiceScopeFactory。");
            registration = new WindowLifecycleRegistration<TResult>(lifecycle, scopeFactory, JsonOptions);
            if (!_lifecycles.TryAdd(value.Id, registration))
            {
                registration.Dispose();
                throw new InvalidOperationException($"窗口 {value.Id} 的生命周期已经存在。");
            }
        }

        var stored = false;
        var ownershipRegistered = false;
        try
        {
            _ownership.Register(
                "PageWindow",
                value.Id,
                value.OwnerWindowId,
                modal && value.OwnerWindowId is not null,
                closeToken => CloseSingleAsync(value.Id, closeToken));
            ownershipRegistered = true;
            // 生命周期必须先于 Pending 发布完成登记，避免窗口立即交互时找不到本次 Show 的回调。
            await _store.CreateAsync(value, token);
            stored = true;
            var request = Map(value);
            var published = _events.Publish(WindowEventNames.Requested, request);
            if (!published.Accepted)
            {
                throw new InvalidOperationException($"Desktop 窗口事件发布失败：{published.Failure}");
            }

            if (_notifier is not null) await _notifier.UpsertAsync(request);
            return new WindowHandle(value.Id, this);
        }
        catch
        {
            if (stored) await _store.RemoveAsync(value.Id, CancellationToken.None);
            if (ownershipRegistered) _ownership.Unregister(value.Id);
            if (_lifecycles.TryRemove(value.Id, out var removed)) removed.Dispose();
            throw;
        }
    }

    internal async Task UpdateAsync(string id, object? data, CancellationToken token)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        var current = await _store.GetAsync(id, token) ?? throw new InvalidOperationException($"窗口 {id} 不存在。");
        StandardDesktopPageValidator.ValidateRequest(current.ViewName, json);
        var value = await _store.UpdateAsync(id, json, token);
        var request = Map(value);
        _events.Publish(WindowEventNames.Updated, request);
        if (_notifier is not null) await _notifier.UpsertAsync(request);
    }

    internal async Task ActivateAsync(string id, CancellationToken token)
    {
        _events.Publish(WindowEventNames.Updated, new { windowId = id, activate = true });
        if (_notifier is not null) await _notifier.ActivateAsync(id);
    }

    internal async Task CloseAsync(string id, CancellationToken token)
    {
        if (!await _ownership.CloseSubtreeAsync(id, token))
        {
            await CloseSingleAsync(id, token);
        }
    }

    private async Task CloseSingleAsync(string id, CancellationToken token)
    {
        if (!await CompleteCoreAsync(new WindowCompletionRequest
            {
                WindowId = id,
                Status = "closed",
                Action = null,
                Result = null
            }, token)) return;
        _events.Publish(WindowEventNames.Closed, new
        {
            windowId = id,
            status = "closed",
            source = "OwnerClosed",
        });
        if (_notifier is not null) await _notifier.CloseAsync(id);
    }

    internal async Task<DialogResult<TResult>> WaitAsync<TResult>(string id, CancellationToken token)
    {
        var current = await _store.GetAsync(id, token) ?? throw new InvalidOperationException($"窗口 {id} 不存在。");
        DesktopCompletion result;
        if (current.Status != "pending")
        {
            result = new DesktopCompletion(current.Status, current.Action, current.ResultJson);
        }
        else
        {
            var waiter = _waiters.GetOrAdd(
                id,
                _ => new TaskCompletionSource<DesktopCompletion>(TaskCreationOptions.RunContinuationsAsynchronously));
            result = await waiter.Task.WaitAsync(token);
        }

        var value = result.ResultJson is null ? default : JsonSerializer.Deserialize<TResult>(result.ResultJson, JsonOptions);
        await _store.ConsumeCompletedAsync(id, token);
        return new DialogResult<TResult>(
            result.Status == "completed" ? WindowEndState.Completed : WindowEndState.Closed,
            result.Action,
            value);
    }

    internal Task<bool> CompleteAsync(
        string id,
        string status,
        string? action,
        JsonElement? result,
        CancellationToken token) => CompleteCoreAsync(new WindowCompletionRequest
        {
            WindowId = id,
            Status = status,
            Action = action,
            Result = result
        }, token);

    private async Task<bool> CompleteCoreAsync(
        WindowCompletionRequest request,
        CancellationToken token)
    {
        var current = await _store.GetAsync(request.WindowId, token)
            ?? throw new InvalidOperationException($"窗口 {request.WindowId} 不存在。");
        if (current.Status != "pending") return false;
        StandardDesktopPageValidator.ValidateResult(current, request.Status, request.Action, request.Result);
        var json = request.Result?.GetRawText();
        if (!await _store.CompleteAsync(request, token)) return false;
        _ownership.Unregister(request.WindowId);

        if (_notificationTimers.TryRemove(request.WindowId, out var timer))
        {
            timer.Cancel();
            timer.Dispose();
        }

        if (_waiters.TryRemove(request.WindowId, out var waiter))
        {
            waiter.TrySetResult(new DesktopCompletion(request.Status, request.Action, json));
        }

        _events.Publish(
            WindowEventNames.Resolved,
            new WindowResolution(request.WindowId, request.Status, request.Action, request.Result));
        var resolved = current with
        {
            Status = request.Status,
            Action = request.Action,
            ResultJson = json,
            Revision = current.Revision + 1,
        };
        if (_lifecycles.TryRemove(request.WindowId, out var lifecycle))
        {
            _ = RunResolvedCallbackAsync(lifecycle, resolved, request);
        }

        return true;
    }

    public async Task<WindowStatus<TResult>?> GetStatusAsync<TResult>(
        string windowId,
        CancellationToken cancellationToken = default)
    {
        var current = await _store.GetAsync(windowId, cancellationToken);
        if (current is null) return null;
        var result = current.ResultJson is null
            ? default
            : JsonSerializer.Deserialize<TResult>(current.ResultJson, JsonOptions);
        return new WindowStatus<TResult>(windowId, current.Status, current.Action, result);
    }

    internal async Task<AgentWindowStateSnapshot?> GetAgentStateAsync(string windowId, CancellationToken token)
    {
        var current = await _store.GetAsync(windowId, token);
        return current is null ? null : new AgentWindowStateSnapshot(windowId, current.Status, current.Revision);
    }

    internal async Task<WindowResultAck> CompleteFromAgentAsync(WindowResultSubmission request, CancellationToken token)
    {
        if (request.State is not ("completed" or "closed"))
        {
            return new WindowResultAck(
                request.SubmissionId,
                "validationRejected",
                "pending",
                request.ExpectedRevision,
                "窗口结果状态无效。",
                "invalid_state");
        }

        var current = await _store.GetAsync(request.WindowId, token);
        if (current is null) return new WindowResultAck(request.SubmissionId, "notFound", "notFound", 0);
        if (current.Status != "pending")
            return new WindowResultAck(request.SubmissionId, "replayed", current.Status, current.Revision);
        if (request.NotAfterUtc < DateTimeOffset.UtcNow)
            return new WindowResultAck(request.SubmissionId, "expired", "pending", current.Revision);
        if (request.ExpectedRevision > 0 && request.ExpectedRevision != current.Revision)
            return new WindowResultAck(request.SubmissionId, "conflict", "pending", current.Revision);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        var remaining = request.NotAfterUtc - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
            return new WindowResultAck(request.SubmissionId, "expired", "pending", current.Revision);
        deadline.CancelAfter(remaining);

        try
        {
            if (_lifecycles.TryGetValue(request.WindowId, out var lifecycle))
            {
                var decision = request.State == "completed"
                    ? await lifecycle.SubmitAsync(current, request.Action, request.Result, deadline.Token)
                    : await lifecycle.CloseAsync(
                        current,
                        request.Action,
                        ParseCloseSource(request.Source),
                        deadline.Token);
                if (!decision.Accepted)
                {
                    return new WindowResultAck(
                        request.SubmissionId,
                        "validationRejected",
                        "pending",
                        current.Revision,
                        decision.Message,
                        decision.Code,
                        Serialize(decision.Data));
                }
            }

            await _ownership.CloseDescendantsAsync(request.WindowId, deadline.Token).ConfigureAwait(false);
            return await CompleteCoreAsync(
                    new WindowCompletionRequest
                    {
                        WindowId = request.WindowId,
                        Status = request.State,
                        Action = request.Action,
                        Result = request.Result,
                        ExpectedRevision = request.ExpectedRevision > 0
                            ? request.ExpectedRevision
                            : current.Revision
                    },
                    deadline.Token)
                ? new WindowResultAck(request.SubmissionId, "accepted", request.State, current.Revision + 1)
                : await CreateConflictOrReplayAckAsync(request, token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !token.IsCancellationRequested)
        {
            return new WindowResultAck(
                request.SubmissionId,
                "expired",
                "pending",
                current.Revision,
                "窗口交互处理已超过有效期。",
                "handler_timeout");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException)
        {
            LogInteractionRejected(_logger, exception, request.WindowId);
            return new WindowResultAck(
                request.SubmissionId,
                "validationRejected",
                "pending",
                current.Revision,
                exception.Message,
                "validation_rejected");
        }
        catch (Exception exception)
        {
            LogInteractionFailed(_logger, exception, request.WindowId);
            return new WindowResultAck(
                request.SubmissionId,
                "handlerFailed",
                "pending",
                current.Revision,
                "服务端窗口交互处理失败。",
                "handler_failed");
        }
    }

    internal async Task<WindowActionAck> InvokeActionFromAgentAsync(
        WindowActionSubmission request,
        CancellationToken token)
    {
        var current = await _store.GetAsync(request.WindowId, token);
        if (current is null)
            return new WindowActionAck(request.RequestId, false, null, "窗口不存在。", "not_found");
        if (current.Status != "pending")
            return new WindowActionAck(request.RequestId, false, null, "窗口已经结束。", "not_pending");
        if (request.ExpectedRevision > 0 && request.ExpectedRevision != current.Revision)
            return new WindowActionAck(request.RequestId, false, null, "窗口数据已经更新。", "conflict");
        if (!_lifecycles.TryGetValue(request.WindowId, out var lifecycle))
            return new WindowActionAck(request.RequestId, false, null, "当前窗口未注册非终态动作处理器。", "handler_missing");

        try
        {
            return await lifecycle.ActionAsync(current, request, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogActionFailed(_logger, exception, request.WindowId, request.Action);
            return new WindowActionAck(
                request.RequestId,
                false,
                null,
                "服务端窗口动作处理失败。",
                "handler_failed");
        }
    }

    internal async Task<WindowPresentedAck> NotifyPresentedAsync(
        WindowPresentedNotification notification,
        CancellationToken token)
    {
        var current = await _store.GetAsync(notification.WindowId, token);
        if (current is null || current.Status != "pending" || current.Revision != notification.Revision)
            return new WindowPresentedAck(notification.WindowId, false, current?.Revision ?? 0);

        if (current.ViewName == StandardViews.Notification)
        {
            var request = JsonSerializer.Deserialize<StandardNotificationRequest>(current.DataJson, JsonOptions);
            if (request?.AutoCloseAfter is { } delay) StartNotificationTimer(current.Id, delay);
        }

        if (_lifecycles.TryGetValue(notification.WindowId, out var lifecycle))
        {
            _ = RunPresentedCallbackAsync(lifecycle, current, notification.PresentedAt);
        }

        return new WindowPresentedAck(notification.WindowId, true, current.Revision);
    }

    private void StartNotificationTimer(string windowId, TimeSpan delay)
    {
        var timer = new CancellationTokenSource();
        if (!_notificationTimers.TryAdd(windowId, timer))
        {
            timer.Dispose();
            return;
        }

        _ = CloseNotificationAfterDelayAsync(windowId, delay, timer);
    }

    private async Task CloseNotificationAfterDelayAsync(
        string windowId,
        TimeSpan delay,
        CancellationTokenSource timer)
    {
        try
        {
            await Task.Delay(delay, timer.Token);
            var result = JsonSerializer.SerializeToElement(new StandardNotificationResult(null, true), JsonOptions);
            await CompleteCoreAsync(new WindowCompletionRequest
            {
                WindowId = windowId,
                Status = "closed",
                Action = "timeout",
                Result = result
            }, CancellationToken.None);
            if (_notifier is not null) await _notifier.CloseAsync(windowId);
        }
        catch (OperationCanceledException) when (timer.IsCancellationRequested)
        {
        }
        finally
        {
            if (_notificationTimers.TryRemove(new KeyValuePair<string, CancellationTokenSource>(windowId, timer)))
                timer.Dispose();
        }
    }

    internal Task<IReadOnlyList<AgentWindow>> PendingAsync(CancellationToken token) => PendingCore(token);

    private async Task<IReadOnlyList<AgentWindow>> PendingCore(CancellationToken token) =>
        (await _store.PendingAsync(token)).Select(Map).ToArray();

    private async Task<WindowResultAck> CreateConflictOrReplayAckAsync(
        WindowResultSubmission request,
        CancellationToken token)
    {
        var latest = await _store.GetAsync(request.WindowId, token);
        return latest is not null && latest.Status != "pending"
            ? new WindowResultAck(request.SubmissionId, "replayed", latest.Status, latest.Revision)
            : new WindowResultAck(request.SubmissionId, "conflict", "pending", latest?.Revision ?? request.ExpectedRevision);
    }

    private async Task RunPresentedCallbackAsync(
        IWindowLifecycleRegistration lifecycle,
        StoredWindow window,
        DateTimeOffset presentedAt)
    {
        try
        {
            await lifecycle.PresentedAsync(window, presentedAt, CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogPresentedCallbackFailed(_logger, exception, window.Id);
        }
    }

    private async Task RunResolvedCallbackAsync(
        IWindowLifecycleRegistration lifecycle,
        StoredWindow window,
        WindowCompletionRequest request)
    {
        try
        {
            await lifecycle.ResolvedAsync(
                window,
                request.Status == "completed" ? WindowEndState.Completed : WindowEndState.Closed,
                request.Action,
                request.Result,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogResolvedCallbackFailed(_logger, exception, window.Id);
        }
        finally
        {
            lifecycle.Dispose();
        }
    }

    private static WindowCloseSource ParseCloseSource(string? source) =>
        Enum.TryParse<WindowCloseSource>(source, true, out var value)
            ? value
            : WindowCloseSource.Page;

    private static JsonElement? Serialize(object? value) =>
        value is null ? null : JsonSerializer.SerializeToElement(value, JsonOptions);

    private static AgentWindow Map(StoredWindow value) => new(
        value.Id,
        value.ViewName,
        value.ViewVersion,
        value.Modal,
        value.Title,
        JsonSerializer.Deserialize<JsonElement>(value.DataJson),
        value.Width,
        value.Height,
        value.Topmost,
        value.Focus,
        value.Flash,
        value.OwnerWindowId,
        value.Status,
        value.CreatedAt,
        value.Revision,
        value.TitleBar);

    Task IWindowHandleOperations.UpdateAsync(
        string windowId,
        object? data,
        CancellationToken cancellationToken) => UpdateAsync(windowId, data, cancellationToken);

    Task IWindowHandleOperations.ActivateAsync(
        string windowId,
        CancellationToken cancellationToken) => ActivateAsync(windowId, cancellationToken);

    Task IWindowHandleOperations.CloseAsync(
        string windowId,
        CancellationToken cancellationToken) => CloseAsync(windowId, cancellationToken);

    Task<DialogResult<TResult>> IWindowHandleOperations.WaitAsync<TResult>(
        string windowId,
        CancellationToken cancellationToken) => WaitAsync<TResult>(windowId, cancellationToken);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Desktop window interaction handler rejected {WindowId}")]
    private static partial void LogInteractionRejected(ILogger logger, Exception exception, string windowId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Desktop window interaction handler failed for {WindowId}")]
    private static partial void LogInteractionFailed(ILogger logger, Exception exception, string windowId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Desktop window action handler failed for {WindowId} {Action}")]
    private static partial void LogActionFailed(ILogger logger, Exception exception, string windowId, string action);

    [LoggerMessage(Level = LogLevel.Error, Message = "Desktop window Presented callback failed for {WindowId}")]
    private static partial void LogPresentedCallbackFailed(ILogger logger, Exception exception, string windowId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Desktop window Resolved callback failed for {WindowId}")]
    private static partial void LogResolvedCallbackFailed(ILogger logger, Exception exception, string windowId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Closing canceled Desktop dialog failed for {WindowId}")]
    private static partial void LogCanceledDialogCloseFailed(ILogger logger, Exception exception, string windowId);
}
