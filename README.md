# BrowserShell

Windows 桌面 Web 外壳：把 Web 页面承载到用户桌面，供任意服务通过 SDK 打开与管理窗口。

本仓库由 SoftwareHub 的 DesktopAgent 迁移而来，独立于 SoftwareHub 发布。

## 组成

| 项目 | 说明 |
|---|---|
| `src/BrowserShell` | 消费方 SDK（NuGet 包 `BrowserShell`）：窗口服务、连接配置与启动器 |
| `src/BrowserShell.Runtime` | Windows 桌面进程（WPF + WebView2），承载 BrowserWindow / PageWindow |
| `tests/BrowserShell.Runtime.Tests` | Runtime 单元测试 |

## 状态

迁移自 `SoftwareHub.DesktopAgent`（1.0.4），本仓库对应 `BrowserShell` 2.0.0。

消费方集成（原 `SoftwareHub.Service.SDK.Hosting/Desktop`）尚未迁入，见迁移报告的后续项。
