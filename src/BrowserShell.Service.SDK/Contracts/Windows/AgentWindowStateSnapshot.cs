namespace BrowserShell.Service.SDK;

/// <summary>接入服务返回的单窗口最新权威状态。</summary>
internal sealed record AgentWindowStateSnapshot(string WindowId, string State, long Revision);
