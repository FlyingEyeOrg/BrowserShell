namespace BrowserShell;

/// <summary>接入服务对单个页面窗口池的覆盖配置。</summary>
public sealed class ViewPoolOptions
{
    public int PrewarmWindowCount { get; set; } = 1;

    public ViewReuseMode ReuseMode { get; set; } = ViewReuseMode.Recreate;
}
