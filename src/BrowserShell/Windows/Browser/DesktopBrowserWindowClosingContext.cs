namespace BrowserShell;

public sealed record BrowserWindowClosingContext(
    string WindowId,
    Uri Url,
    BrowserWindowCloseSource Source);
