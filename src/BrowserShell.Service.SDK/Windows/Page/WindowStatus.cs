namespace BrowserShell.Service.SDK;

/// <summary>供异步 HTTP 工作流补查的窗口权威状态。</summary>
public sealed record WindowStatus<TResult>(
    string WindowId,
    string Status,
    string? Action,
    TResult? Result);
