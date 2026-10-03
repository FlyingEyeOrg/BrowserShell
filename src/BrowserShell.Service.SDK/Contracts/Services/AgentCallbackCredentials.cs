namespace BrowserShell.Service.SDK;

/// <summary>BrowserShell 换取当前接入服务页面资源令牌的凭证。</summary>
internal sealed record AgentCallbackCredentials(string ClientId, string ClientSecret);
