namespace BrowserShell.Service.SDK;

/// <summary>Agent 在结果响应未知时发起的只读权威状态查询。</summary>
internal sealed record WindowStateQuery(string WindowId);
