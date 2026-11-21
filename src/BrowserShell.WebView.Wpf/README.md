# BrowserShell.WebView.Wpf

在 [WindowChromeKit](https://www.nuget.org/packages/WindowChromeKit.Wpf) 的 `ChromeWindow`
上承载 WebView2 的 Windows 桌面 Web 外壳类库。

窗口的原生边框、拖动、缩放、Snap Layout、DWM 阴影与 DPI 处理由 `ChromeWindow` 提供；
本包只负责内容承载与窗口级能力。

## 快速开始

```csharp
using System.Windows;
using BrowserShell.WebView.Wpf;
using WindowChromeKit.Wpf;

var shell = await WebViewShell.CreateAsync(Application.Current.Dispatcher);

var window = await shell.OpenAsync(new WebViewWindowOptions
{
    Url = new Uri("https://www.example.com/index.html"),
    Title = "SAP",
    Width = 1280,
    Height = 800,
});
```

标题栏默认使用 `ChromeTitleBarStyle.Chrome`（含 WPF 版自带配色），无需显式指定。

## 配置（WebViewShellOptions）

`WebViewShellOptions` 控制 WebView2 的用户数据目录。默认值即可直接用，通常只需要设一项。

### 目录怎么拼

```
最终路径 = UserDataFolderRoot \ ProductFolderName \ ApplicationFolderName
默认     = %LOCALAPPDATA%      \ BrowserShell     \ {exe 文件名}
例       = C:\Users\me\AppData\Local\BrowserShell\SoftwareHub
```

| 属性 | 作用 | 默认 |
|---|---|---|
| `UserDataFolderRoot` | 路径的**根位置** | `%LOCALAPPDATA%` |
| `ProductFolderName` | 第二段：**产品/厂商**目录 | `BrowserShell` |
| `ApplicationFolderName` | 第三段：**应用**目录 | 当前 exe 文件名 |
| `UserDataFolder` | **完整路径**；设置后上面三项全部忽略 | `null` |

### 三种常见配法

```csharp
// ① 全默认
WebViewShell.CreateAsync(dispatcher);

// ② 推荐：固定应用名。否则重命名 exe 会指向全新目录，
//    缓存与登录态随之"丢失"。
WebViewShell.CreateAsync(dispatcher, new WebViewShellOptions
{
    ApplicationFolderName = "SoftwareHub",
});

// ③ 完全自定义位置（接管上面三项）
WebViewShell.CreateAsync(dispatcher, new WebViewShellOptions
{
    UserDataFolder = @"D:\Cache\WebView2",
});
```

### 几个要点

- **不要放 `%TEMP%`**：那里存的是 Cookie、localStorage 等需要长期保留的数据，
  而临时目录会被磁盘清理与 Storage Sense 删除。需要临时目录时显式设
  `UserDataFolderRoot = UserDataFolderLocation.Temp`（仅建议用于自动化测试）。
- **目录跨进程复用**：同一应用多次启动共用同一目录，因此缓存与登录态能保留，
  也避免了 WebView2 在全新目录下的初始化开销。
- **所有窗口共用一个 profile**：与浏览器"一个 profile 服务所有标签页"一致，
  磁盘占用不随开窗次数增长。

## 依赖注入

库提供 ASP.NET Core 风格的 `IServiceCollection` 扩展。

```csharp
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// UI 线程的 Dispatcher 交给容器
services.AddSingleton(Application.Current.Dispatcher);

services.AddBrowserShellWebView(options =>
{
    options.ApplicationFolderName = "SoftwareHub";
});

var provider = services.BuildServiceProvider();
```

注册后有两种取用方式：

```csharp
// ① 直接注入（单例、延迟创建：首次解析时才初始化 WebView2 环境）
public sealed class PortalService(WebViewShell shell)
{
    public Task OpenAsync(Uri url) => shell.OpenAsync(new WebViewWindowOptions { Url = url });
}

// ② 注入工厂（需要在 UI 线程异步获取时）
public sealed class LazyPortalService(IWebViewShellFactory factory)
{
    public async Task OpenAsync(Uri url)
    {
        var shell = await factory.CreateAsync();
        await shell.OpenAsync(new WebViewWindowOptions { Url = url });
    }
}
```

要点：

- `AddBrowserShellWebView()` 可重复调用，内部用 `TryAdd`，不会重复注册。
- `WebViewShell` 与 `IWebViewShellFactory` 都是 **Singleton**，且工厂幂等——
  多次 `CreateAsync()` 只初始化一次 WebView2 环境。
- 容器释放时（`provider.DisposeAsync()`）会连带释放 `WebViewShell`。
- 解析与使用都必须在 **UI 线程**上；从后台线程进入时用 `IWebViewShellFactory.CreateAsync`。

## 窗口定位

`Left` / `Top` 在**创建时**指定窗口首次出现的位置（设备无关像素，虚拟桌面坐标）：

```csharp
var window = await shell.OpenAsync(new WebViewWindowOptions
{
    Url = new Uri("https://www.example.com/index.html"),
    Left = 588,
    Top = 237,          // 首次出现即在 (588, 237)
});
```

优先级：**显式 `Left` + `Top` > `Center` > 系统默认**。

- 两个坐标**必须成对**给出；只给一个（或给 `NaN`/无穷大）视为未指定，回退到 `Center`
- 指定显式坐标时 `Center` 被忽略，且**不做工作区裁剪**——坐标按原值使用，合法性由调用方负责
- 默认为 `null`，行为与未提供时完全一致

> `OpenAsync` 返回时窗口已经在屏幕上（它是"创建 + 首次导航 + 显示"一体）。
> 在它返回后再设 `window.Left` / `window.Top` 会让用户看到窗口先出现在一处、再跳到目标位置。
> 这两个属性在显示之前应用，因此没有跳动。

## 主要类型

| 类型 | 说明 |
|---|---|
| `WebViewShell` | 外壳管理器：创建/跟踪/关闭窗口，持有进程内唯一的 WebView2 环境 |
| `WebViewWindow` | 单个外壳窗口，派生自 `ChromeWindow` |
| `WebViewWindowOptions` | 窗口设置（地址、尺寸、标题栏样式、Origin 白名单、关闭裁决） |
| `WebViewShellOptions` | 环境设置（用户数据目录的根位置、产品/应用目录、完整路径覆盖） |
| `WebViewWpfServiceCollectionExtensions` | `AddBrowserShellWebView()`：ASP.NET Core 风格的 DI 注册 |
| `IWebViewShellFactory` | 异步创建外壳的工厂（单例、幂等），供需要 UI 线程异步获取的场景 |

## 页面侧控制

外壳向页面注入 `window.browserShell`：

```js
browserShell.window.minimize();
browserShell.window.maximize();
browserShell.window.restore();
const result = await browserShell.window.close();
```

## 宿主要求

宿主应用需为 `net8.0-windows`、启用 WPF，并在 STA 线程上创建窗口
（`WebViewShell` 内部会切回传入的 `Dispatcher`）。
