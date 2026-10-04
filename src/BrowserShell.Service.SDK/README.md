# BrowserShell

Windows 10/11 桌面窗口代理的 .NET 8 NuGet。包拥有统一窗口 SDK、连接协议，并在 `tools/win-x86`、`tools/win-x64` 携带 self-contained WPF Runtime；消费项目无需 Node.js、系统 .NET Runtime 或 Electron。

业务稳定入口是 `IWindowService` 及 BrowserWindow/PageWindow 的 Options、Handle、Lifecycle 和标准页面类型。Hub、Lease、Sequence、Snapshot、Command、Submission 与 Ack 是程序集内部协议，不属于 1.0 公共 API；公共签名由 `PublicAPI.Shipped.txt` 固化。

Web 服务通过 `IWindowService` 使用同一 Agent 会话下的两类窗口：

- `BrowserWindows`：为 SPA/MPA 打开类似 Electron 的长期 Web 应用窗口；支持显示、隐藏、激活、最小化、最大化、恢复、刷新、同源导航、标题和关闭。
- `PageWindows`：沿用原有标准窗口和自定义业务弹窗，保留模态、Pending、强类型结果与每次 Show 独立生命周期。

公开页面只设置 `Url`。需要一次性登录/启动地址的页面可设置 `BootstrapUriFactory`；服务侧工厂会在首次打开、Agent 恢复和 `ReloadFromBootstrapAsync` 时重新执行。BrowserShell 只导航最终地址，不理解或保存页面的 OAuth、Cookie、OIDC 与业务权限。

```csharp
using BrowserShell;

var mainWindow = await desktop.BrowserWindows.OpenAsync(
    new BrowserWindowOptions
    {
        Url = new Uri("https://www.example.com/index.html"),
        Title = "SAP",
        Width = 1280,
        Height = 800,
    },
    new BrowserWindowLifecycle
    {
        ClosingAsync = (_, _) => ValueTask.FromResult(
            CanClose()
                ? BrowserWindowCloseDecision.Allow
                : BrowserWindowCloseDecision.Reject("业务仍在处理中。", "BUSY")),
    });
```

BrowserWindow 和 PageWindow 共享 Session、窗口注册表及 Owner/模态关系。BrowserShell Runtime 只保存当前进程的窗口投影和临时 WebView2 Profile，不保存业务结果；服务进程是窗口期望状态和关闭裁决的权威来源。

Runtime 在 WPF STA Dispatcher 上管理窗口、每会话 Owner 森林和 WebView2 生命周期，同时以 Kestrel 暴露固定路径版本 `/api/v1`。未指定 Owner 的窗口是独立根；PageWindow 与 BrowserWindow 共用关系注册表，只有模态 PageWindow 边通过 HWND 引用计数门控输入。BrowserShell 是 SignalR 服务端，接入服务使用独立客户端凭证连接 `/api/v1/hubs/clients`；错误遵循 Problem Details，客户端管理资源仅允许 Agent owner。

`Automatic` 在 Development 启动当前服务专属 Runtime；Release 仅在 Host 注入完整连接信息时连接共享 Agent，否则禁用。Release 若要启动专属实例，必须显式选择 `Launch`。

消费项目可通过 MSBuild 属性 `UseSelfContainedBrowserShell` 控制是否把包内 Runtime 复制到输出目录的 `BrowserShell.Runtime/` 子目录：Debug 未设置时默认启用，Release 未设置时默认关闭，外部显式值优先。该独立辅助进程不会放入 .NET/NuGet 保留的 `runtimes/` 目录。Release 若使用 `Launch`，应启用该属性或通过 `BrowserShell:ExecutablePath` 指定可执行文件。

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <UseSelfContainedBrowserShell>true</UseSelfContainedBrowserShell>
</PropertyGroup>
```

命令行可使用 `/p:UseSelfContainedBrowserShell=false` 覆盖项目设置。属性关闭时只是不执行复制；切换设置后验证输出内容应先清理旧输出目录。

```powershell
dotnet publish ..\BrowserShell.Runtime -c Release -r win-x64 --self-contained true
dotnet publish ..\BrowserShell.Runtime -c Release -r win-x86 --self-contained true
```

完整业务接入示例见 `docs/BrowserShell使用手册.md`；内部架构、无状态原则与资源治理见 `docs/BrowserShell技术方案.md`。
