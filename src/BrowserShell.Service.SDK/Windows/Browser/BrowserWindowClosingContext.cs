namespace BrowserShell.Service.SDK;

public sealed record BrowserWindowClosingContext(
    string WindowId,
    Uri Url,
    BrowserWindowCloseSource Source);
