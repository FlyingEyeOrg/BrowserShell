namespace BrowserShell.Service.SDK;

/// <summary>当前服务进程会话中的 BrowserShell 窗口记录。</summary>
internal sealed record StoredWindow(
    string Id,
    string ViewName,
    int ViewVersion,
    bool Modal,
    string? Title,
    string DataJson,
    int? Width,
    int? Height,
    bool Topmost,
    bool Focus,
    bool Flash,
    string? OwnerWindowId,
    string Status,
    long CreatedAt,
    string? Action = null,
    string? ResultJson = null,
    long Revision = 1,
    WindowTitleBarOptions? TitleBar = null);
