# BrowserShell

Windows 桌面 Web 外壳：把 Web 页面承载到用户桌面，供任意服务通过 SDK 打开与管理窗口。

本仓库由 SoftwareHub 的 DesktopAgent 迁移而来，独立于 SoftwareHub 发布。

## 组成

| 项目 | TFM | 说明 |
|---|---|---|
| `src/BrowserShell.Service.SDK` | `net8.0` | 服务侧窗口能力 SDK。服务通过它连接外壳、打开与管理桌面窗口 |
| `src/BrowserShell.WebView.Wpf` | `net8.0-windows` | WPF + WebView2 桌面代理类库，提供基础 Web 套壳能力 |
| `tests/BrowserShell.WebView.Wpf.Tests` | `net8.0-windows` | 桌面代理单元测试 |

`BrowserShell.WebView.Wpf` 是类库，**不含可执行入口**。宿主进程由引用方自行编写：

```csharp
using System.Windows;
using BrowserShell.WebView.Wpf;

public partial class App : Application
{
    private ShellHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _host = await ShellHost.StartAsync(Dispatcher, e.Args.SingleOrDefault(), CancellationToken.None);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null) await _host.DisposeAsync();
        base.OnExit(e);
    }
}
```

宿主 WPF 应用需满足：`net8.0-windows`、启用 WPF、引用 `BrowserShell.WebView.Wpf`，并按需提供 `app.manifest`（PerMonitorV2 DPI 感知）。

## 依赖方向

```
宿主 exe（引用方自建）
   └──> BrowserShell.WebView.Wpf  (net8.0-windows)
   └──> BrowserShell.Service.SDK  (net8.0)

BrowserShell.WebView.Wpf ──> BrowserShell.Service.SDK
BrowserShell.Service.SDK ──> 不引用 WPF
```

`BrowserShell.Service.SDK` 保持 `net8.0` 且无 WPF 依赖，因此 ASP.NET Core 服务引用它不会引入桌面依赖链。

## 状态

迁移自 `SoftwareHub.DesktopAgent`（1.0.4），本仓库版本 `2.0.0`。

消费方集成层（原 `SoftwareHub.Service.SDK.Hosting/Desktop`）不在本仓库内：它是 SoftwareHub 对 BrowserShell 的适配层，需另行接入。
