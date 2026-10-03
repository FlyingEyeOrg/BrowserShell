namespace BrowserShell.Service.SDK;

public sealed record PageWindowSubmittingContext<TResult>(
    string WindowId,
    string ViewName,
    long Revision,
    IServiceProvider Services,
    string? Action,
    TResult? Result)
    : PageWindowLifecycleContext(WindowId, ViewName, Revision, Services);
