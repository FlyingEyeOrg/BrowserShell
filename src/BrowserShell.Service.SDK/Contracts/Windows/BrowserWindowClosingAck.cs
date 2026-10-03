namespace BrowserShell.Service.SDK;

internal sealed record BrowserWindowClosingAck(
    string RequestId,
    bool Accepted,
    string? Message = null,
    string? Code = null);
