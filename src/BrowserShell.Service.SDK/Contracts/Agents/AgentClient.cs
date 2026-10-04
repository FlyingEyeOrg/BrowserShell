namespace BrowserShell.Service.SDK;

internal sealed record AgentClient(
    string ClientId,
    IReadOnlyList<string> ServiceInstanceIds,
    DateTimeOffset? ExpiresAt,
    bool Revoked);
