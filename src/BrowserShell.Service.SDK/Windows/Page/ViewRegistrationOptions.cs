namespace BrowserShell.Service.SDK;

/// <summary>BrowserShell View 注册时的可选配置。</summary>
public sealed class ViewRegistrationOptions
{
    public int Version { get; set; } = 1;

    public int PrewarmWindowCount { get; set; } = 1;

    public ViewReuseMode ReuseMode { get; set; } = ViewReuseMode.Recreate;

    public string? IconPath { get; set; }

    public WindowTitleBarOptions? TitleBar { get; set; }
}
