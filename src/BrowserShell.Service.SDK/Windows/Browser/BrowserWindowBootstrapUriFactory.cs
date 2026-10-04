namespace BrowserShell.Service.SDK;

/// <summary>
/// 为 BrowserWindow 的真实目标地址生成一次性启动地址。
/// 委托只保存在 Web 服务进程中，每次首次呈现、Agent 恢复或显式重新启动导航时调用。
/// </summary>
public delegate ValueTask<Uri> BrowserWindowBootstrapUriFactory(
    Uri targetUri,
    CancellationToken cancellationToken);
