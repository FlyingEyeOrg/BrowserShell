namespace BrowserShell;

/// <summary>VS Code 风格桌面标题栏的颜色。颜色格式为 #RRGGBB 或 #RRGGBBAA。</summary>
public sealed class WindowTitleBarOptions
{
    public string? ActiveBackground { get; init; }
    public string? ActiveForeground { get; init; }
    public string? InactiveBackground { get; init; }
    public string? InactiveForeground { get; init; }
    public string? Border { get; init; }
}
