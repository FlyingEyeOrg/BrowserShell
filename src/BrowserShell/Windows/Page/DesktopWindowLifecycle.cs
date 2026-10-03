namespace BrowserShell;

/// <summary>绑定到一次 Desktop 窗口显示请求的服务端生命周期回调。</summary>
public sealed class WindowLifecycle<TResult>
{
    public WindowPresentedCallback? PresentedAsync { get; init; }

    public WindowActionCallback? ActionAsync { get; init; }

    public Func<WindowSubmittingContext<TResult>, CancellationToken,
        ValueTask<WindowInteractionDecision>>? SubmittingAsync { get; init; }

    public WindowClosingCallback? ClosingAsync { get; init; }

    public Func<WindowResolvedContext<TResult>, CancellationToken, ValueTask>? ResolvedAsync { get; init; }
}
