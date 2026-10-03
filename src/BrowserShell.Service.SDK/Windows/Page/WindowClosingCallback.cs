namespace BrowserShell.Service.SDK;

public delegate ValueTask<WindowInteractionDecision> WindowClosingCallback(
    WindowClosingContext context,
    CancellationToken cancellationToken);
