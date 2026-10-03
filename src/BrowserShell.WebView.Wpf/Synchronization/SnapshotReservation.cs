namespace BrowserShell.WebView.Wpf;

/// <summary>一次快照处理占用的全局资源租约。</summary>
internal sealed class SnapshotReservation : IDisposable
{
    private readonly SnapshotResourceCoordinator _owner;
    private readonly long _bytes;
    private int _disposed;

    internal SnapshotReservation(SnapshotResourceCoordinator owner, long bytes)
    {
        _owner = owner;
        _bytes = bytes;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _owner.Release(_bytes);
        }
    }
}
