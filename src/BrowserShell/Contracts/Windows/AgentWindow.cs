using System.Text.Json;

namespace BrowserShell;

/// <summary>接入服务发送给 BrowserShell 的完整权威窗口投影。</summary>
internal sealed record AgentWindow(
    string WindowId,
    string ViewName,
    int ViewVersion,
    bool Modal,
    string? Title,
    JsonElement? Data,
    int? Width,
    int? Height,
    bool Topmost,
    bool Focus,
    bool Flash,
    string? OwnerWindowId,
    string Status,
    long CreatedAt,
    long Revision,
    WindowTitleBarOptions? TitleBar = null);
