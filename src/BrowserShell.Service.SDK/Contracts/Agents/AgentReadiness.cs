namespace BrowserShell.Service.SDK;

internal sealed record AgentReadiness(string Status, bool WebView2Ready, DateTimeOffset CheckedAt);
