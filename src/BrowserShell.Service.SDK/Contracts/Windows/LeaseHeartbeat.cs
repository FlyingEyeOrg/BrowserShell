namespace BrowserShell.Service.SDK;

/// <summary>接入服务为当前 Agent Session 续租的应用层心跳。</summary>
internal sealed record LeaseHeartbeat(string LeaseId);
