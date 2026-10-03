namespace BrowserShell.Service.SDK;

public delegate ValueTask PageWindowPresentedCallback(
    PageWindowPresentedContext context,
    CancellationToken cancellationToken);
