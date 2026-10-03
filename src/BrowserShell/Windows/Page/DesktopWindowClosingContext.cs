namespace BrowserShell;

public sealed record WindowClosingContext(
    string WindowId,
    string ViewName,
    long Revision,
    IServiceProvider Services,
    WindowCloseSource Source,
    string? Action)
    : WindowLifecycleContext(WindowId, ViewName, Revision, Services);
