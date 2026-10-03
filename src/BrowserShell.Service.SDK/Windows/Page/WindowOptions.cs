namespace BrowserShell.Service.SDK;

/// <summary>窗口外观建议；最终尺寸、Owner、模态关系和屏幕边界由 Agent 决定。</summary>
public sealed class WindowOptions
{
    public string? OwnerWindowId { get; set; }
    public string? Title { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    /// <summary>是否请求系统级置顶；默认 false，Agent 仅应用本次请求值。</summary>
    public bool Topmost { get; init; }
    public bool Focus { get; init; } = true;
    public bool Flash { get; init; }
    public WindowTitleBarOptions? TitleBar { get; init; }
}
