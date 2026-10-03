namespace BrowserShell.WebView.Wpf;

/// <summary>Agent 内存中的短期访问令牌及其授权上下文。</summary>
internal sealed record AccessTokenEntry(
    AuthContext Context,
    DateTimeOffset ExpiresAt);
