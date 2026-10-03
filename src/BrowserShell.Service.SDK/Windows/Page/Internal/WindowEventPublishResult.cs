namespace BrowserShell.Service.SDK;

internal sealed record WindowEventPublishResult(bool Accepted, string? Failure = null);
