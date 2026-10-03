using BrowserShell;

namespace BrowserShell;

/// <summary>自治服务注册给 BrowserShell 的静态页面定义。</summary>
public sealed record ViewDefinition(
    string Name,
    string Path,
    int Version,
    int PrewarmWindowCount,
    ViewReuseMode ReuseMode,
    string? IconPath = null)
{
    public WindowTitleBarOptions? TitleBar { get; init; }
}
