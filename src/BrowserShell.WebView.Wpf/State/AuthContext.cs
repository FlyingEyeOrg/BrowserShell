namespace BrowserShell.WebView.Wpf;

/// <summary>通过 Agent 本地认证后的调用方上下文。</summary>
internal sealed record AuthContext(
    string ClientId,
    bool Owner,
    IReadOnlyList<string> ServiceInstanceIds,
    long ClientVersion);
