namespace BrowserShell.Runtime;

/// <summary>Agent 调用服务端窗口回调时的超时和熔断配置。</summary>
internal sealed class AgentCallbackOptions
{
    public int ResponseTimeoutSeconds { get; init; } = 10;

    public int ReconcileTimeoutSeconds { get; init; } = 30;
}
