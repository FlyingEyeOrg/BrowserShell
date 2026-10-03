using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using BrowserShell.Service.SDK;

namespace BrowserShell.Service.SDK;

internal sealed class WindowLifecycleRegistration<TResult> : IWindowLifecycleRegistration
{
    private readonly WindowLifecycle<TResult> _lifecycle;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _interactionGate = new(1, 1);
    private readonly ConcurrentDictionary<string, Task<WindowActionAck>> _actionResults = new(StringComparer.Ordinal);
    private int _presented;
    private int _disposed;

    public WindowLifecycleRegistration(
        WindowLifecycle<TResult> lifecycle,
        IServiceScopeFactory scopeFactory,
        JsonSerializerOptions jsonOptions)
    {
        _lifecycle = lifecycle;
        _scopeFactory = scopeFactory;
        _jsonOptions = jsonOptions;
    }

    public async Task<WindowInteractionDecision> SubmitAsync(
        StoredWindow window,
        string? action,
        JsonElement? result,
        CancellationToken cancellationToken)
    {
        if (_lifecycle.SubmittingAsync is null) return WindowInteractionDecision.Allow;

        await _interactionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await using var scope = _scopeFactory.CreateAsyncScope();
            var value = result is null ? default : result.Value.Deserialize<TResult>(_jsonOptions);
            var context = new WindowSubmittingContext<TResult>(
                window.Id,
                window.ViewName,
                window.Revision,
                scope.ServiceProvider,
                action,
                value);
            return await _lifecycle.SubmittingAsync(context, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _interactionGate.Release();
        }
    }

    public async Task<WindowInteractionDecision> CloseAsync(
        StoredWindow window,
        string? action,
        WindowCloseSource source,
        CancellationToken cancellationToken)
    {
        if (_lifecycle.ClosingAsync is null) return WindowInteractionDecision.Allow;

        await _interactionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = new WindowClosingContext(
                window.Id,
                window.ViewName,
                window.Revision,
                scope.ServiceProvider,
                source,
                action);
            return await _lifecycle.ClosingAsync(context, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _interactionGate.Release();
        }
    }

    public Task<WindowActionAck> ActionAsync(
        StoredWindow window,
        WindowActionSubmission submission,
        CancellationToken cancellationToken)
    {
        if (_lifecycle.ActionAsync is null)
        {
            return Task.FromResult(new WindowActionAck(
                submission.RequestId,
                false,
                null,
                "当前窗口未注册非终态动作处理器。",
                "handler_missing"));
        }

        return _actionResults.GetOrAdd(
            submission.RequestId,
            _ => InvokeActionAsync(window, submission, cancellationToken));
    }

    private async Task<WindowActionAck> InvokeActionAsync(
        StoredWindow window,
        WindowActionSubmission submission,
        CancellationToken cancellationToken)
    {
        await _interactionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = new WindowActionContext(
                window.Id,
                window.ViewName,
                window.Revision,
                scope.ServiceProvider,
                submission.RequestId,
                submission.Action,
                submission.Data);
            var result = await _lifecycle.ActionAsync!(context, cancellationToken).ConfigureAwait(false);
            return new WindowActionAck(
                submission.RequestId,
                result.Accepted,
                Serialize(result.Result),
                result.Message,
                result.Code,
                Serialize(result.Data));
        }
        finally
        {
            _interactionGate.Release();
        }
    }

    public async Task PresentedAsync(
        StoredWindow window,
        DateTimeOffset presentedAt,
        CancellationToken cancellationToken)
    {
        if (_lifecycle.PresentedAsync is null || Interlocked.Exchange(ref _presented, 1) != 0) return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = new WindowPresentedContext(
            window.Id,
            window.ViewName,
            window.Revision,
            scope.ServiceProvider,
            presentedAt);
        await _lifecycle.PresentedAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public async Task ResolvedAsync(
        StoredWindow window,
        WindowEndState endState,
        string? action,
        JsonElement? result,
        CancellationToken cancellationToken)
    {
        if (_lifecycle.ResolvedAsync is null) return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var value = result is null ? default : result.Value.Deserialize<TResult>(_jsonOptions);
        var context = new WindowResolvedContext<TResult>(
            window.Id,
            window.ViewName,
            window.Revision,
            scope.ServiceProvider,
            endState,
            action,
            value);
        await _lifecycle.ResolvedAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private JsonElement? Serialize(object? value) =>
        value is null ? null : JsonSerializer.SerializeToElement(value, _jsonOptions);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _interactionGate.Dispose();
            _actionResults.Clear();
        }
    }
}
