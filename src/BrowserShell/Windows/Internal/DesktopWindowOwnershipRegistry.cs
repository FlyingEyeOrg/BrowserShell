namespace BrowserShell;

/// <summary>服务进程内 PageWindow 与 BrowserWindow 共用的权威 Owner 森林。</summary>
internal sealed class WindowOwnershipRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Node> _nodes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _closing = new(StringComparer.Ordinal);

    public void Register(
        string windowKind,
        string windowId,
        string? ownerWindowId,
        bool modal,
        Func<CancellationToken, Task> closeFromOwner)
    {
        lock (_gate)
        {
            if (_nodes.ContainsKey(windowId))
                throw new InvalidOperationException($"Desktop 窗口标识重复：{windowId}。");
            if (string.Equals(windowId, ownerWindowId, StringComparison.Ordinal))
                throw new InvalidOperationException("Desktop 窗口不能拥有自身。");
            if (ownerWindowId is not null && !_nodes.TryGetValue(ownerWindowId, out _))
                throw new InvalidOperationException($"Desktop 父窗口 {ownerWindowId} 不存在于当前会话。");
            if (ownerWindowId is not null && _closing.Contains(ownerWindowId))
                throw new InvalidOperationException($"Desktop 父窗口 {ownerWindowId} 正在关闭。");

            var node = new Node(windowKind, windowId, ownerWindowId, modal, closeFromOwner);
            _nodes.Add(windowId, node);
            if (ownerWindowId is not null) _nodes[ownerWindowId].Children.Add(windowId);
        }
    }

    public void Unregister(string windowId)
    {
        lock (_gate)
        {
            if (!_nodes.Remove(windowId, out var node)) return;
            if (node.OwnerWindowId is not null && _nodes.TryGetValue(node.OwnerWindowId, out var owner))
                owner.Children.Remove(windowId);
            _closing.Remove(windowId);
        }
    }

    public async Task<bool> CloseSubtreeAsync(string windowId, CancellationToken token)
    {
        Node[] ordered;
        lock (_gate)
        {
            if (!_nodes.ContainsKey(windowId)) return false;
            ordered = GetPostOrder(windowId).ToArray();
            foreach (var node in ordered) _closing.Add(node.WindowId);
        }

        try
        {
            foreach (var node in ordered)
            {
                token.ThrowIfCancellationRequested();
                await node.CloseFromOwner(token).ConfigureAwait(false);
            }
            return true;
        }
        finally
        {
            lock (_gate)
            {
                foreach (var node in ordered) _closing.Remove(node.WindowId);
            }
        }
    }

    public async Task CloseDescendantsAsync(string windowId, CancellationToken token)
    {
        Node[] ordered;
        lock (_gate)
        {
            if (!_nodes.TryGetValue(windowId, out var root)) return;
            _closing.Add(windowId);
            ordered = root.Children
                .Order(StringComparer.Ordinal)
                .SelectMany(GetPostOrder)
                .ToArray();
            foreach (var node in ordered) _closing.Add(node.WindowId);
        }

        try
        {
            foreach (var node in ordered)
            {
                token.ThrowIfCancellationRequested();
                await node.CloseFromOwner(token).ConfigureAwait(false);
            }
        }
        finally
        {
            lock (_gate)
            {
                foreach (var node in ordered) _closing.Remove(node.WindowId);
            }
        }
    }

    private List<Node> GetPostOrder(string windowId)
    {
        var result = new List<Node>();
        var stack = new Stack<(string WindowId, bool Visited)>();
        stack.Push((windowId, false));
        while (stack.TryPop(out var entry))
        {
            var node = _nodes[entry.WindowId];
            if (entry.Visited)
            {
                result.Add(node);
                continue;
            }

            stack.Push((entry.WindowId, true));
            foreach (var childId in node.Children.OrderDescending(StringComparer.Ordinal))
                stack.Push((childId, false));
        }
        return result;
    }

    private sealed record Node(
        string WindowKind,
        string WindowId,
        string? OwnerWindowId,
        bool Modal,
        Func<CancellationToken, Task> CloseFromOwner)
    {
        public HashSet<string> Children { get; } = new(StringComparer.Ordinal);
    }
}
