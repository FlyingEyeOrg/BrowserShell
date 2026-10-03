using System.Collections.Concurrent;
using BrowserShell.Service.SDK;

namespace BrowserShell.WebView.Wpf;

/// <summary>按接入服务实例隔离连接、租约和窗口管理器。</summary>
internal sealed class AgentClientSessionRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, AgentClientSession> _sessions = new(StringComparer.Ordinal);

    public AgentClientSession Replace(string connectionId, AgentClientRegistration registration)
    {
        var replacement = new AgentClientSession(connectionId, registration);
        _sessions.AddOrUpdate(
            registration.ServiceInstanceId,
            replacement,
            (_, previous) =>
            {
                previous.Expire();
                previous.Dispose();
                return replacement;
            });
        return replacement;
    }

    public bool TryGet(string serviceInstanceId, out AgentClientSession session) =>
        _sessions.TryGetValue(serviceInstanceId, out session!);

    public AgentClientSession Require(string serviceInstanceId, string connectionId, string leaseId)
    {
        if (!_sessions.TryGetValue(serviceInstanceId, out var session)
            || session.State == AgentSessionState.Expired
            || !string.Equals(session.ConnectionId, connectionId, StringComparison.Ordinal)
            || !string.Equals(session.LeaseId, leaseId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("当前连接的服务会话或租约已经失效。");
        }

        return session;
    }

    public AgentClientSession? Disconnect(string connectionId)
    {
        var session = _sessions.Values.FirstOrDefault(item =>
            string.Equals(item.ConnectionId, connectionId, StringComparison.Ordinal));
        session?.MarkDisconnected();
        return session;
    }

    public IReadOnlyList<AgentClientSession> Snapshot() => [.. _sessions.Values];

    public bool Expire(string serviceInstanceId, string leaseId)
    {
        if (!_sessions.TryGetValue(serviceInstanceId, out var session)
            || !string.Equals(session.LeaseId, leaseId, StringComparison.Ordinal)
            || !_sessions.TryRemove(serviceInstanceId, out session))
        {
            return false;
        }

        session.Expire();
        session.Dispose();
        return true;
    }

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }

        _sessions.Clear();
    }
}
