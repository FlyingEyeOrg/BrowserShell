namespace BrowserShell.Service.SDK;

/// <summary>服务获取 BrowserShell 连接的方式。</summary>
public enum BrowserShellConnectionMode
{
    /// <summary>开发环境拉起独占 Agent；生产环境有外部凭据时连接，否则禁用。</summary>
    Automatic,
    /// <summary>拉起当前服务独占的 Agent。</summary>
    Launch,
    /// <summary>连接已经存在的共享 Agent。</summary>
    Connect,
    /// <summary>禁用桌面窗口能力。</summary>
    Disabled,
}
