namespace BrowserShell.Service.SDK;

/// <summary>单个页面管理器的注册与预热状态。</summary>
internal sealed record AgentViewRegistrationResult(
    string Name,
    int Version,
    AgentViewRegistrationState State,
    int RetainedWindowCount);
