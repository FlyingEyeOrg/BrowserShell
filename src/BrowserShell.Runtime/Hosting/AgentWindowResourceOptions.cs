namespace BrowserShell.Runtime;

/// <summary>BrowserShell 进程级窗口、快照和内存保护配置。</summary>
internal sealed class AgentWindowResourceOptions
{
    public int MaxServiceVisibleWindowCount { get; init; } = 32;
    public int ReservedSystemWarningWindowCount { get; init; } = 1;
    public int MaxTotalWindowInstanceCount { get; init; } = 128;
    public int MaxIdleWebViewCount { get; init; } = 64;
    public int PrewarmConcurrency { get; init; } = 2;
    public int MaxViewsPerSession { get; init; } = 64;
    public int MaxPrewarmWindowCountPerView { get; init; } = 16;
    public int MaxPendingWindowCountPerSession { get; init; } = 10_000;
    public int MaxTotalPendingWindowCount { get; init; } = 50_000;
    public int MaxSerializedWindowBytes { get; init; } = 320 * 1024;
    public int MaxSnapshotBytesPerSession { get; init; } = 64 * 1024 * 1024;
    public long MaxTotalSessionStateBytes { get; init; } = 512L * 1024 * 1024;
    public int MaxConcurrentSnapshotUploads { get; init; } = 4;
    public long MaxTotalSnapshotStagingBytes { get; init; } = 256L * 1024 * 1024;
    public int SnapshotQueueTimeoutSeconds { get; init; } = 30;
    public int SnapshotUploadTimeoutSeconds { get; init; } = 30;
    public int WindowDisposeTimeoutSeconds { get; init; } = 10;
    public int WindowAuditIntervalSeconds { get; init; } = 30;
    public int ResourceWaitWarningSeconds { get; init; } = 60;
}
