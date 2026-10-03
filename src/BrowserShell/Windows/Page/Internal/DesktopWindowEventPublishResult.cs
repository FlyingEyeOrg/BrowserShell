namespace BrowserShell;

internal sealed record WindowEventPublishResult(bool Accepted, string? Failure = null);
