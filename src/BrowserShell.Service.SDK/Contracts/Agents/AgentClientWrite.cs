namespace BrowserShell.Service.SDK;

internal sealed record AgentClientWrite(
    string ClientSecret,
    IReadOnlyList<string> ServiceInstanceIds,
    DateTimeOffset? ExpiresAt);
