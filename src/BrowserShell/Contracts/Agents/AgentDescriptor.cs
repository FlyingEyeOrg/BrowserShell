namespace BrowserShell;

internal sealed record AgentDescriptor(string AgentId, string Version, bool Ready, DateTimeOffset StartedAt);
