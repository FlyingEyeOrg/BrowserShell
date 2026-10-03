namespace BrowserShell.Service.SDK;

/// <summary>集中解析 BrowserShell 配置和环境默认行为。</summary>
internal static class BrowserShellOptionsResolver
{
    public static BrowserShellAction Resolve(BrowserShellOptions options, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.StartupTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "StartupTimeout 必须大于零。");
        }

        var action = options.Mode switch
        {
            BrowserShellConnectionMode.Disabled => BrowserShellAction.Disabled,
            BrowserShellConnectionMode.Launch => BrowserShellAction.Launch,
            BrowserShellConnectionMode.Connect => BrowserShellAction.Connect,
            BrowserShellConnectionMode.Automatic when isDevelopment => BrowserShellAction.Launch,
            BrowserShellConnectionMode.Automatic when HasConnection(options) => BrowserShellAction.Connect,
            _ => BrowserShellAction.Disabled,
        };
        if (action == BrowserShellAction.Connect && !HasConnection(options))
        {
            throw new InvalidOperationException("Connect 模式必须配置 Endpoint、ClientId 和 ClientSecret。");
        }

        return action;
    }

    private static bool HasConnection(BrowserShellOptions value) =>
        value.Endpoint is not null
        && !string.IsNullOrWhiteSpace(value.ClientId)
        && !string.IsNullOrWhiteSpace(value.ClientSecret);
}
