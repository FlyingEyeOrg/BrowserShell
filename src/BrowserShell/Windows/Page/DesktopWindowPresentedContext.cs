namespace BrowserShell;

public sealed record WindowPresentedContext(
    string WindowId,
    string ViewName,
    long Revision,
    IServiceProvider Services,
    DateTimeOffset PresentedAt)
    : WindowLifecycleContext(WindowId, ViewName, Revision, Services);
