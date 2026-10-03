namespace BrowserShell.Service.SDK;

/// <summary>绑定到一次 BrowserShell 窗口显示请求的服务端生命周期回调。</summary>
public sealed class PageWindowLifecycle<TResult>
{
    public PageWindowPresentedCallback? PresentedAsync { get; init; }

    public PageWindowActionCallback? ActionAsync { get; init; }

    public Func<PageWindowSubmittingContext<TResult>, CancellationToken,
        ValueTask<PageWindowInteractionDecision>>? SubmittingAsync { get; init; }

    public PageWindowClosingCallback? ClosingAsync { get; init; }

    public Func<PageWindowResolvedContext<TResult>, CancellationToken, ValueTask>? ResolvedAsync { get; init; }
}
