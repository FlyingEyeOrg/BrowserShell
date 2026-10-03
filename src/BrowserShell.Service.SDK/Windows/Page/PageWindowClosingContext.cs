namespace BrowserShell.Service.SDK;

public sealed record PageWindowClosingContext(
    string WindowId,
    string ViewName,
    long Revision,
    IServiceProvider Services,
    PageWindowCloseSource Source,
    string? Action)
    : PageWindowLifecycleContext(WindowId, ViewName, Revision, Services);
