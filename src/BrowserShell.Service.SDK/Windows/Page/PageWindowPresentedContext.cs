namespace BrowserShell.Service.SDK;

public sealed record PageWindowPresentedContext(
    string WindowId,
    string ViewName,
    long Revision,
    IServiceProvider Services,
    DateTimeOffset PresentedAt)
    : PageWindowLifecycleContext(WindowId, ViewName, Revision, Services);
