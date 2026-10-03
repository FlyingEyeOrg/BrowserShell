namespace BrowserShell.Service.SDK;

internal sealed record BrowserWindowCommand(
    string WindowId,
    BrowserWindowOperation Operation,
    string? Value = null);
