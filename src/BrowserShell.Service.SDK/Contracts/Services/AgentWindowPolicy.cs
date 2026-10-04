namespace BrowserShell.Service.SDK;

/// <summary>当前接入服务允许使用的窗口调度上限。</summary>
internal sealed record AgentWindowPolicy(int MaxVisibleWindowCount);
