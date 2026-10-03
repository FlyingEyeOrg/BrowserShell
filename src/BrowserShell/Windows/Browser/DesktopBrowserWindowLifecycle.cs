namespace BrowserShell;

/// <summary>绑定到一个 BrowserWindow 实例的生命周期。</summary>
public sealed class BrowserWindowLifecycle
{
    public Func<BrowserWindowClosingContext, CancellationToken,
        ValueTask<BrowserWindowCloseDecision>>? ClosingAsync { get; init; }

    public Func<string, CancellationToken, ValueTask>? ClosedAsync { get; init; }
}
