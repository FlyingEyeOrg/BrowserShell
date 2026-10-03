using BrowserShell;

namespace BrowserShell.Runtime;

/// <summary>校验完成后可原子替换的服务权威 Pending 投影。</summary>
internal sealed record WindowSnapshot(string SnapshotId, long Revision, IReadOnlyList<AgentWindow> Windows);
