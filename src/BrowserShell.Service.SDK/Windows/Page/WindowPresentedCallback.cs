namespace BrowserShell.Service.SDK;

public delegate ValueTask WindowPresentedCallback(
    WindowPresentedContext context,
    CancellationToken cancellationToken);
