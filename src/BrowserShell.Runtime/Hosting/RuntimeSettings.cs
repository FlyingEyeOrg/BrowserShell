using System.IO;
using SoftwareHub.Auth;

namespace BrowserShell.Runtime;

internal sealed class RuntimeSettings
{
    public RuntimeAuthenticationMode AuthenticationMode { get; init; } =
        RuntimeAuthenticationMode.EncryptedNetworkOAuth;
    public string AgentId { get; init; } = Guid.NewGuid().ToString("N");
    public string Endpoint { get; init; } = "http://127.0.0.1:0";
    public string AdminClientId { get; init; } = "owner";
    public string AdminClientSecret { get; init; } = string.Empty;
    public IReadOnlyList<InitialClientSettings> Clients { get; init; } = [];
    public int ParentProcessId { get; init; }
    public int HeartbeatIntervalSeconds { get; init; } = 5;
    public int LeaseTimeoutSeconds { get; init; } = 15;
    public int ReconnectGracePeriodSeconds { get; init; } = 30;
    public string? WebView2RuntimePath { get; init; }
    public AgentWindowResourceOptions WindowResources { get; init; } = new();
    public AgentCallbackOptions Callback { get; init; } = new();
    public string LogDirectory { get; init; } = Path.Combine(Environment.CurrentDirectory, "logs");
}
