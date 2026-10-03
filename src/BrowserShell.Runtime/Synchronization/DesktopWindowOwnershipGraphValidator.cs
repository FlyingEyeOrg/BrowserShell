using System.IO;
using BrowserShell;

namespace BrowserShell.Runtime;

/// <summary>统一校验 PageWindow 与 BrowserWindow 组成的同会话父子窗口图。</summary>
internal static class WindowOwnershipGraphValidator
{
    public static void Validate(
        IReadOnlyList<AgentWindow> pageWindows,
        IReadOnlyList<AgentBrowserWindow> browserWindows)
    {
        var owners = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var window in browserWindows)
        {
            if (string.IsNullOrWhiteSpace(window.WindowId)
                || !owners.TryAdd(window.WindowId, window.OwnerWindowId))
                throw new InvalidDataException("Desktop 窗口标识必须在服务会话内唯一。");
        }

        foreach (var window in pageWindows)
        {
            if (window.OwnerWindowId is not null && !window.Modal)
                throw new InvalidDataException("只有模态 PageWindow 可以指定 OwnerWindowId。");
            if (!owners.TryAdd(window.WindowId, window.OwnerWindowId))
                throw new InvalidDataException("Desktop 窗口标识必须在服务会话内唯一。");
        }

        var state = new Dictionary<string, byte>(StringComparer.Ordinal);
        foreach (var windowId in owners.Keys)
        {
            Visit(windowId);
        }

        void Visit(string windowId)
        {
            if (state.TryGetValue(windowId, out var current))
            {
                if (current == 1) throw new InvalidDataException("Desktop 父子窗口关系存在循环。");
                return;
            }

            state[windowId] = 1;
            if (owners[windowId] is { } ownerWindowId)
            {
                if (!owners.ContainsKey(ownerWindowId))
                    throw new InvalidDataException($"Desktop 父窗口 {ownerWindowId} 不存在于当前服务会话。");
                Visit(ownerWindowId);
            }
            state[windowId] = 2;
        }
    }

    public static IReadOnlyList<AgentWindow> OrderPageWindowsParentsFirst(IReadOnlyList<AgentWindow> windows) =>
        OrderParentsFirst(
            windows,
            window => window.WindowId,
            window => window.OwnerWindowId,
            Comparer<AgentWindow>.Create((left, right) =>
            {
                var created = left.CreatedAt.CompareTo(right.CreatedAt);
                return created != 0 ? created : StringComparer.Ordinal.Compare(left.WindowId, right.WindowId);
            }));

    public static IReadOnlyList<AgentBrowserWindow> OrderBrowserWindowsParentsFirst(
        IReadOnlyList<AgentBrowserWindow> windows) =>
        OrderParentsFirst(
            windows,
            window => window.WindowId,
            window => window.OwnerWindowId,
            Comparer<AgentBrowserWindow>.Create((left, right) =>
                StringComparer.Ordinal.Compare(left.WindowId, right.WindowId)));

    private static List<T> OrderParentsFirst<T>(
        IReadOnlyList<T> windows,
        Func<T, string> getId,
        Func<T, string?> getOwnerId,
        IComparer<T> comparer)
    {
        var byId = windows.ToDictionary(getId, StringComparer.Ordinal);
        var ordered = new List<T>(windows.Count);
        var added = new HashSet<string>(StringComparer.Ordinal);
        foreach (var window in windows.OrderBy(item => item, comparer)) Visit(window);
        return ordered;

        void Visit(T window)
        {
            var id = getId(window);
            if (!added.Add(id)) return;
            if (getOwnerId(window) is { } ownerId && byId.TryGetValue(ownerId, out var owner)) Visit(owner);
            ordered.Add(window);
        }
    }
}
