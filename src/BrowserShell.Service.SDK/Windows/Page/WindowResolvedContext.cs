namespace BrowserShell.Service.SDK;

public sealed record WindowResolvedContext<TResult>(
    string WindowId,
    string ViewName,
    long Revision,
    IServiceProvider Services,
    WindowEndState EndState,
    string? Action,
    TResult? Result)
    : WindowLifecycleContext(WindowId, ViewName, Revision, Services);
