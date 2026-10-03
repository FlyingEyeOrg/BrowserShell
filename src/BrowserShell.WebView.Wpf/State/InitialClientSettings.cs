namespace BrowserShell.WebView.Wpf;

/// <summary>随 Agent 启动配置注入的初始客户端凭证。</summary>
internal sealed class InitialClientSettings
{
    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;

    public IReadOnlyList<string> ServiceInstanceIds { get; init; } = [];
}
