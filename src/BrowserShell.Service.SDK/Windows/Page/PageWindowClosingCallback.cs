namespace BrowserShell.Service.SDK;

public delegate ValueTask<PageWindowInteractionDecision> PageWindowClosingCallback(
    PageWindowClosingContext context,
    CancellationToken cancellationToken);
