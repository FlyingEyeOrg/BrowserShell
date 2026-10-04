namespace BrowserShell.Service.SDK;

/// <summary>接入服务侧的窗口调度和 SDK 默认能力配置。</summary>
public sealed class WindowManagementOptions
{
    public int MaxVisibleWindowCount { get; set; } = 16;

    public bool EnableStandardWindow { get; set; } = true;
}
