namespace BrowserShell.Service.SDK;

/// <summary>Agent 对应用层心跳的确认。</summary>
internal sealed record LeaseHeartbeatResult(string LeaseId, bool Accepted, DateTimeOffset LeaseExpiresAt);
