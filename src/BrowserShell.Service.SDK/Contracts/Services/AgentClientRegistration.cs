namespace BrowserShell.Service.SDK;

/// <summary>接入服务向 BrowserShell 提交的完整注册信息。</summary>
internal sealed record AgentClientRegistration(
    string ServiceInstanceId,
    string Endpoint,
    DateTimeOffset ServiceUtcNow,
    AgentCallbackCredentials CallbackCredentials,
    AgentWindowPolicy WindowPolicy,
    IReadOnlyList<AgentViewDefinition> Views,
    string? IconPath = null);
