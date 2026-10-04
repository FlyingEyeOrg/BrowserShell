namespace BrowserShell.Service.SDK;

/// <summary>BrowserShell 连接与启动设置。</summary>
public sealed class BrowserShellOptions
{
    public BrowserShellConnectionMode Mode { get; set; } = BrowserShellConnectionMode.Automatic;
    public Uri? Endpoint { get; set; }
    public string? AgentId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? ExecutablePath { get; set; }
    public bool EnableInTests { get; set; }
    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public WindowManagementOptions WindowManagement { get; set; } = new();
    public Dictionary<string, ViewPoolOptions> Views { get; set; } = new(StringComparer.Ordinal);
}
