namespace BrowserShell;

internal sealed record AgentClient(
    string ClientId,
    IReadOnlyList<string> ServiceInstanceIds,
    DateTimeOffset? ExpiresAt,
    bool Revoked);
