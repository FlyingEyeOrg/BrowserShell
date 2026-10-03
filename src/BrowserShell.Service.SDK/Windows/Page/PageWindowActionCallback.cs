namespace BrowserShell.Service.SDK;

public delegate ValueTask<PageWindowActionResult> PageWindowActionCallback(
    PageWindowActionContext context,
    CancellationToken cancellationToken);
