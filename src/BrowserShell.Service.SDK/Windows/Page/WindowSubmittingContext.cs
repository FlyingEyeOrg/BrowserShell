namespace BrowserShell.Service.SDK;

public sealed record WindowSubmittingContext<TResult>(
    string WindowId,
    string ViewName,
    long Revision,
    IServiceProvider Services,
    string? Action,
    TResult? Result)
    : WindowLifecycleContext(WindowId, ViewName, Revision, Services);
