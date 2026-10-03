namespace BrowserShell.Service.SDK;

public sealed record PageWindowResolvedContext<TResult>(
    string WindowId,
    string ViewName,
    long Revision,
    IServiceProvider Services,
    PageWindowEndState EndState,
    string? Action,
    TResult? Result)
    : PageWindowLifecycleContext(WindowId, ViewName, Revision, Services);
