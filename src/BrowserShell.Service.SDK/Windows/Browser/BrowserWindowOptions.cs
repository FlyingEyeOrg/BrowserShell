namespace BrowserShell.Service.SDK;

/// <summary>长期 Web 应用窗口的创建选项。</summary>
public sealed class BrowserWindowOptions
{
    /// <summary>可选父窗口；同一 BrowserShell 服务会话内有效。</summary>
    public string? OwnerWindowId { get; init; }

    public required Uri Url { get; init; }

    /// <summary>
    /// 可选的一次性启动地址工厂。<see cref="Url"/> 始终保存真实目标地址；
    /// 工厂生成的地址只用于当前导航，不作为可恢复业务状态。
    /// </summary>
    public BrowserWindowBootstrapUriFactory? BootstrapUriFactory { get; init; }

    public string? Title { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public int? MinWidth { get; init; }

    public int? MinHeight { get; init; }

    public bool Center { get; init; } = true;

    public bool Topmost { get; init; }

    public bool Focus { get; init; } = true;

    public WindowTitleBarOptions? TitleBar { get; init; }

    /// <summary>允许顶层页面导航到的 Origin；未配置时只允许 <see cref="Url"/> 的 Origin。</summary>
    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];
}
