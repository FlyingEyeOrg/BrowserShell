namespace BrowserShell.Service.SDK;

/// <summary>Web 程序发送给 BrowserShell 的 BrowserWindow 当前投影。</summary>
internal sealed record AgentBrowserWindow(
    string WindowId,
    string? OwnerWindowId,
    string Url,
    string? Title,
    int? Width,
    int? Height,
    int? MinWidth,
    int? MinHeight,
    bool Center,
    bool Topmost,
    bool Focus,
    IReadOnlyList<string> AllowedOrigins,
    long Revision,
    WindowTitleBarOptions? TitleBar = null,
    string? TargetUrl = null);
