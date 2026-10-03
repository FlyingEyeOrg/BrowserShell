namespace BrowserShell.Service.SDK;

/// <summary>
/// 当前 Web 服务与 BrowserShell Agent 会话下的统一窗口入口。
/// BrowserWindows 承载 SPA/MPA 应用窗口，PageWindows 承载业务弹窗。
/// </summary>
public interface IWindowService
{
    IBrowserWindowService BrowserWindows { get; }

    IPageWindowService PageWindows { get; }
}
