namespace BrowserShell.Runtime;

/// <summary>接入服务会话的生命周期状态。</summary>
internal enum AgentSessionState
{
    Registering,
    Synchronizing,
    Online,
    Disconnected,
    Expired,
}
