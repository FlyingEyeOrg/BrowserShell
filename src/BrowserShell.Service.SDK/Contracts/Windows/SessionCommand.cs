namespace BrowserShell.Service.SDK;

/// <summary>当前 Lease 内严格排序的增量命令。</summary>
internal sealed record SessionCommand<T>(string LeaseId, long Sequence, T Payload);
