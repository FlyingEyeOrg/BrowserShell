using BrowserShell.Service.SDK;

namespace BrowserShell.Runtime;

/// <summary>一个接入服务在当前 Agent 进程中的易失会话。</summary>
internal sealed class AgentClientSession : IDisposable
{
    private readonly CancellationTokenSource _leaseCancellation = new();
    private readonly CancellationToken _leaseToken;
    private long _lastHeartbeatTimestamp = Environment.TickCount64;
    private long _lastSequence;

    public AgentClientSession(string connectionId, AgentClientRegistration registration)
    {
        ConnectionId = connectionId;
        Registration = registration;
        LeaseId = Guid.NewGuid().ToString("N");
        _leaseToken = _leaseCancellation.Token;
    }

    public string ConnectionId { get; }
    public string LeaseId { get; }
    public AgentClientRegistration Registration { get; }
    public AgentSessionState State { get; private set; } = AgentSessionState.Registering;
    public CancellationToken LeaseCancellation => _leaseToken;

    public void BeginSynchronization() => State = AgentSessionState.Synchronizing;
    public void CompleteSynchronization() => State = AgentSessionState.Online;
    public void MarkDisconnected()
    {
        Heartbeat();
        State = AgentSessionState.Disconnected;
    }

    public void Heartbeat()
    {
        Interlocked.Exchange(ref _lastHeartbeatTimestamp, Environment.TickCount64);
    }

    public bool IsLeaseExpired(TimeSpan timeout) =>
        Environment.TickCount64 - Interlocked.Read(ref _lastHeartbeatTimestamp) > timeout.TotalMilliseconds;

    public bool TryAcceptSequence(long sequence)
    {
        if (sequence <= 0)
        {
            return false;
        }

        while (true)
        {
            var current = Interlocked.Read(ref _lastSequence);
            if (sequence != current + 1)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _lastSequence, sequence, current) == current)
            {
                return true;
            }
        }
    }

    public void Expire()
    {
        State = AgentSessionState.Expired;
        _leaseCancellation.Cancel();
    }

    public void Dispose()
    {
        _leaseCancellation.Cancel();
        _leaseCancellation.Dispose();
    }
}
