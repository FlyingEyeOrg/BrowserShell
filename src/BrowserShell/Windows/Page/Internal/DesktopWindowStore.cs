namespace BrowserShell;

/// <summary>
/// 保存当前服务进程会话中的 Desktop 窗口状态。进程结束后状态不可恢复。
/// </summary>
internal sealed class WindowStore
{
    private const int MaximumWindowCount = 1_000;
    private const int MaximumPendingWindowCount = 100;
    private const int MaximumCompletedWindowCount = 500;
    private const int MaximumRequestBytes = 1024 * 1024;
    private const int MaximumResultBytes = 256 * 1024;
    private static readonly TimeSpan CompletedWindowRetention = TimeSpan.FromMinutes(10);
    private readonly object _gate = new();
    private readonly Dictionary<string, StoredWindow> _windows = new(StringComparer.Ordinal);

    public Task CreateAsync(StoredWindow value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (System.Text.Encoding.UTF8.GetByteCount(value.DataJson) > MaximumRequestBytes)
                throw new InvalidOperationException($"Desktop 窗口请求超过 {MaximumRequestBytes} 字节上限。");
            RemoveExpiredCompletedWindows(DateTimeOffset.UtcNow);
            if (_windows.Values.Count(window => window.Status == "pending") >= MaximumPendingWindowCount)
                throw new InvalidOperationException($"当前进程的 Pending Desktop 窗口已达到上限 {MaximumPendingWindowCount}。");
            if (_windows.Count >= MaximumWindowCount)
            {
                RemoveOldestCompletedWindows(_windows.Count - MaximumWindowCount + 1);
            }

            if (_windows.Count >= MaximumWindowCount)
            {
                throw new InvalidOperationException(
                    $"当前进程的 Desktop 窗口已达到容量上限 {MaximumWindowCount}，请先关闭现有窗口。");
            }

            if (!_windows.TryAdd(value.Id, value))
            {
                throw new InvalidOperationException($"Desktop 窗口 {value.Id} 已存在。");
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StoredWindow>> PendingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            IReadOnlyList<StoredWindow> result = _windows.Values
                .Where(window => string.Equals(window.Status, "pending", StringComparison.Ordinal))
                .OrderBy(window => window.CreatedAt)
                .ThenBy(window => window.Id, StringComparer.Ordinal)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    public Task<StoredWindow?> GetAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _windows.TryGetValue(id, out var value);
            return Task.FromResult(value);
        }
    }

    public Task RemoveAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _windows.Remove(id);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ConsumeCompletedAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_windows.TryGetValue(id, out var value)
                || string.Equals(value.Status, "pending", StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            _windows[id] = value with { ResultJson = null };
            return Task.FromResult(true);
        }
    }

    public Task<bool> CompleteAsync(
        WindowCompletionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WindowId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Status);
        cancellationToken.ThrowIfCancellationRequested();
        var result = request.Result?.GetRawText();
        if (result is not null && System.Text.Encoding.UTF8.GetByteCount(result) > MaximumResultBytes)
            throw new InvalidOperationException($"Desktop 窗口结果超过 {MaximumResultBytes} 字节上限。");

        lock (_gate)
        {
            if (!_windows.TryGetValue(request.WindowId, out var current)
                || !string.Equals(current.Status, "pending", StringComparison.Ordinal)
                || request.ExpectedRevision is not null && current.Revision != request.ExpectedRevision.Value)
            {
                return Task.FromResult(false);
            }

            _windows[request.WindowId] = current with
            {
                Status = request.Status,
                Action = request.Action,
                ResultJson = result,
                Revision = current.Revision + 1,
            };
            RemoveOldestCompletedWindows(Math.Max(0, _windows.Values.Count(window => window.Status != "pending") - MaximumCompletedWindowCount));
            return Task.FromResult(true);
        }
    }

    public Task<StoredWindow> UpdateAsync(
        string id,
        string data,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(data);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (System.Text.Encoding.UTF8.GetByteCount(data) > MaximumRequestBytes)
                throw new InvalidOperationException($"Desktop 窗口请求超过 {MaximumRequestBytes} 字节上限。");
            if (!_windows.TryGetValue(id, out var current)
                || !string.Equals(current.Status, "pending", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"窗口 {id} 不存在或已关闭。");
            }

            var updated = current with
            {
                DataJson = data,
                Revision = current.Revision + 1,
            };
            _windows[id] = updated;
            return Task.FromResult(updated);
        }
    }

    private void RemoveExpiredCompletedWindows(DateTimeOffset now)
    {
        var cutoff = now.Subtract(CompletedWindowRetention).ToUnixTimeMilliseconds();
        foreach (var id in _windows.Values
                     .Where(window => !string.Equals(window.Status, "pending", StringComparison.Ordinal)
                         && window.CreatedAt < cutoff)
                     .Select(window => window.Id)
                     .ToArray())
        {
            _windows.Remove(id);
        }
    }

    private void RemoveOldestCompletedWindows(int count)
    {
        foreach (var id in _windows.Values
                     .Where(window => !string.Equals(window.Status, "pending", StringComparison.Ordinal))
                     .OrderBy(window => window.CreatedAt)
                     .ThenBy(window => window.Id, StringComparer.Ordinal)
                     .Take(count)
                     .Select(window => window.Id)
                     .ToArray())
        {
            _windows.Remove(id);
        }
    }
}
