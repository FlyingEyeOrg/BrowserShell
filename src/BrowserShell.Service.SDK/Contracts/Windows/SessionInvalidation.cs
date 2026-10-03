namespace BrowserShell.Service.SDK;

/// <summary>Agent 通知接入服务当前 Lease 已经失效。</summary>
internal sealed record SessionInvalidation(string LeaseId, string Reason);
