namespace BrowserShell;

public delegate ValueTask<WindowInteractionDecision> WindowClosingCallback(
    WindowClosingContext context,
    CancellationToken cancellationToken);
