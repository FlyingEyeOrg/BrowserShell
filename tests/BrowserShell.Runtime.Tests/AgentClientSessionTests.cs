using BrowserShell;
using BrowserShell.Runtime;

namespace BrowserShell.Runtime.Tests;

public sealed class AgentClientSessionTests
{
    [Fact]
    public void 增量命令必须从一开始严格递增()
    {
        using var session = new AgentClientSession("connection", CreateRegistration());

        Assert.True(session.TryAcceptSequence(1));
        Assert.False(session.TryAcceptSequence(1));
        Assert.False(session.TryAcceptSequence(3));
        Assert.True(session.TryAcceptSequence(2));
    }

    [Fact]
    public void 新连接替换旧连接并取消旧租约()
    {
        using var registry = new AgentClientSessionRegistry();
        var previous = registry.Replace("connection-1", CreateRegistration());

        var current = registry.Replace("connection-2", CreateRegistration());

        Assert.True(previous.LeaseCancellation.IsCancellationRequested);
        Assert.NotEqual(previous.LeaseId, current.LeaseId);
        Assert.Same(current, registry.Require("service-1", "connection-2", current.LeaseId));
    }

    private static AgentClientRegistration CreateRegistration() => new(
        "service-1", "http://127.0.0.1:5000", DateTimeOffset.UtcNow,
        new AgentCallbackCredentials("callback", "secret"), new AgentWindowPolicy(4), []);
}
