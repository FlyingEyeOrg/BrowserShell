namespace BrowserShell.Service.SDK;

/// <summary>页面管理器完成注册后的当前状态。</summary>
internal enum AgentViewRegistrationState
{
    Ready,
    Warming,
    Deferred,
    Unhealthy,
}
