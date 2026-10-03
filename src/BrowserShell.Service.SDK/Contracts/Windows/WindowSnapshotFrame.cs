namespace BrowserShell.Service.SDK;

/// <summary>流式同步 Pending 快照时使用的有界协议帧。</summary>
internal sealed record WindowSnapshotFrame(
    WindowSnapshotFrameKind Kind,
    string SnapshotId,
    string? LeaseId = null,
    int? ProtocolVersion = null,
    long? SnapshotRevision = null,
    int? ExpectedWindowCount = null,
    long? ExpectedContentBytes = null,
    int? Index = null,
    byte[]? Payload = null,
    int? ActualWindowCount = null,
    long? ActualContentBytes = null,
    string? ContentHash = null);
