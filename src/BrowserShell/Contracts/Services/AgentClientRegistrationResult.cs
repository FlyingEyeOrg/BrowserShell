namespace BrowserShell;

/// <summary>BrowserShell 接受注册后返回的会话信息。</summary>
internal sealed record AgentClientRegistrationResult(
    string ServiceInstanceId,
    string LeaseId,
    int HeartbeatIntervalSeconds,
    int LeaseTimeoutSeconds,
    int ReconnectGracePeriodSeconds,
    IReadOnlyList<AgentViewRegistrationResult> Views);
