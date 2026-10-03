namespace BrowserShell.Service.SDK;

public delegate ValueTask<WindowActionResult> WindowActionCallback(
    WindowActionContext context,
    CancellationToken cancellationToken);
