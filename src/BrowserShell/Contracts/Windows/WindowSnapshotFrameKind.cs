namespace BrowserShell;

/// <summary>完整 Pending 快照流中的帧类型。</summary>
internal enum WindowSnapshotFrameKind
{
    Header,
    Chunk,
    Trailer,
}
