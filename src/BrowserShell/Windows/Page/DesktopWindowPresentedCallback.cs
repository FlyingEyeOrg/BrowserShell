namespace BrowserShell;

public delegate ValueTask WindowPresentedCallback(
    WindowPresentedContext context,
    CancellationToken cancellationToken);
