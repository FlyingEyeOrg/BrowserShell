namespace BrowserShell.Service.SDK;

/// <summary>接入服务注册给 BrowserShell 的一个 HTML 页面定义。</summary>
internal sealed record AgentViewDefinition(
    string Name,
    string Path,
    int Version,
    int PrewarmWindowCount = 1,
    ViewReuseMode ReuseMode = ViewReuseMode.Recreate,
    string? IconPath = null,
    WindowTitleBarOptions? TitleBar = null);
