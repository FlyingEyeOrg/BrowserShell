namespace BrowserShell.WebView.Wpf;

/// <summary>Agent 本地客户端凭据及其接入服务访问范围。</summary>
internal sealed record ClientEntry(
    string ClientId,
    string SecretHash,
    IReadOnlyList<string> ServiceInstanceIds,
    DateTimeOffset? ExpiresAt,
    bool Revoked,
    bool Owner,
    long Version);
