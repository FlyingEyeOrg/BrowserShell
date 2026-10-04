namespace BrowserShell.Service.SDK;

internal sealed record BrowserWindowClosingRequest(
    string RequestId,
    string WindowId,
    string Url,
    string Source,
    DateTimeOffset NotAfterUtc);
