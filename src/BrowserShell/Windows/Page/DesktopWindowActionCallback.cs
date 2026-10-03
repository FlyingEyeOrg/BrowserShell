namespace BrowserShell;

public delegate ValueTask<WindowActionResult> WindowActionCallback(
    WindowActionContext context,
    CancellationToken cancellationToken);
