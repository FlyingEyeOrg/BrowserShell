namespace BrowserShell;

internal sealed record BrowserWindowCommand(
    string WindowId,
    BrowserWindowOperation Operation,
    string? Value = null);
