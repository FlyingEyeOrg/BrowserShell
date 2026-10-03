using System.IO;

namespace BrowserShell.WebView.Wpf;

/// <summary>限制全局并发快照及暂存字节，防止单个服务挤占 Agent 内存。</summary>
internal sealed class SnapshotResourceCoordinator : IDisposable
{
    private readonly SemaphoreSlim _concurrency;
    private readonly long _maximumBytes;
    private long _reservedBytes;

    public SnapshotResourceCoordinator(RuntimeSettings settings)
    {
        _concurrency = new SemaphoreSlim(Math.Max(1, settings.WindowResources.MaxConcurrentSnapshotUploads));
        _maximumBytes = Math.Max(1, settings.WindowResources.MaxTotalSnapshotStagingBytes);
    }

    public async Task<SnapshotReservation> ReserveAsync(long bytes, CancellationToken token)
    {
        if (bytes <= 0 || bytes > _maximumBytes)
        {
            throw new InvalidDataException("快照声明的内容长度超出 Agent 全局限制。");
        }

        await _concurrency.WaitAsync(token);
        var total = Interlocked.Add(ref _reservedBytes, bytes);
        if (total > _maximumBytes)
        {
            Interlocked.Add(ref _reservedBytes, -bytes);
            _concurrency.Release();
            throw new InvalidOperationException("Agent 正在处理的快照已达到全局资源上限。");
        }

        return new SnapshotReservation(this, bytes);
    }

    internal void Release(long bytes)
    {
        Interlocked.Add(ref _reservedBytes, -bytes);
        _concurrency.Release();
    }

    public void Dispose() => _concurrency.Dispose();
}
