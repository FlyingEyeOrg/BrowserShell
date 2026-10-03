using System.Text.Json;

namespace BrowserShell.Service.SDK;

/// <summary>窗口进入终态后发布给页面和后台工作流的可靠事件。</summary>
public sealed record WindowResolution(
    string WindowId,
    string Status,
    string? Action,
    JsonElement? Result);
