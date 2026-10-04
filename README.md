# BrowserShell

Windows 桌面 Web 外壳：把 Web 页面承载到用户桌面，供任意服务通过 SDK 打开与管理窗口。

本仓库由 SoftwareHub 的 DesktopAgent 迁移而来，独立于 SoftwareHub 发布。

## 组成

| 项目 | TFM | 说明 |
|---|---|---|
| `src/BrowserShell.WebView.Wpf` | `net8.0-windows` | 桌面 Web 外壳类库。在 `ChromeWindow` 上承载 WebView2 |
| `src/BrowserShell.Service.SDK` | `net8.0` | 服务侧窗口能力 SDK。服务通过它连接外壳、打开与管理桌面窗口 |
| `tests/BrowserShell.WebView.Wpf.Tests` | `net8.0-windows` | 外壳单元测试 |
| `samples/BrowserShell.WebView.Wpf.Sample` | `net8.0-windows` | 可运行的示例宿主：选择标题栏样式/配色、打开窗口、关闭裁决 |

## 运行示例

```bash
dotnet run --project samples/BrowserShell.WebView.Wpf.Sample
```

示例是一个 `ChromeWindow` 宿主，提供：

- **打开 Web 窗口** —— 按填入的地址打开外壳窗口，可指定标题栏样式（`Chrome` / `Windows` /
  `VsCode`）、配色（`Default` / `ElementPlusPrimary` / `ElementPlusDark` / `ElementPlusNeutral`）、
  置顶、是否可调整大小、是否限制同源导航，以及关闭裁决策略（直接关闭 / 允许 / 拒绝）
- **打开本地示例页** —— 打开随示例复制的 `Assets/demo.html`，该页通过
  `browserShell.window.*` 演示最小化、最大化、还原与请求关闭（含裁决结果回传）

## 窗口基础

`BrowserShell.WebView.Wpf` **不自带窗口边框**。原生拖动、缩放、Snap Layout、DWM 阴影、
DPI 处理与工作区约束全部由 [WindowChromeKit](https://www.nuget.org/packages/WindowChromeKit.Wpf)
的 `ChromeWindow` 提供，本项目只负责：

- WebView2 的创建、隔离 Profile 与串行初始化
- 首次导航、同源约束、新窗口处理
- 页面侧窗口控制桥（`window.browserShell.window.*`）
- 加载遮罩
- 窗口级命令（显示/隐藏/最小化/最大化/还原/标题）

```csharp
using System.Windows;
using BrowserShell.WebView.Wpf;
using WindowChromeKit.Wpf;

var shell = await WebViewShell.CreateAsync(Application.Current.Dispatcher);

// 标题栏默认使用 Chrome 样式（ChromeTitleBarStyle.Chrome + 自带配色）；
// 需要时再显式指定其他骨架或配色。
var window = await shell.OpenAsync(new WebViewWindowOptions
{
    Url = new Uri("https://www.example.com/index.html"),
    Title = "SAP",
    Width = 1280,
    Height = 800,
});

await window.NavigateAsync(new Uri("https://www.example.com/other.html"));
await window.ReloadAsync();
await window.CloseAsync();
```

### 关闭裁决

窗口默认直接关闭。需要业务裁决时提供 `ClosingAsync`，返回 `false` 即拒绝本次关闭：

```csharp
var window = await shell.OpenAsync(new WebViewWindowOptions
{
    Url = new Uri("https://www.example.com/index.html"),
    ClosingAsync = (context, _) => Task.FromResult(CanClose()),
});
```

`context.Source` 表示关闭来源：`WindowChrome` 为标题栏按钮，`Page` 为页面脚本。

### 页面侧控制

外壳会向页面注入 `window.browserShell`，页面可据此控制自己的窗口：

```js
browserShell.window.minimize();
browserShell.window.maximize();
browserShell.window.restore();
const result = await browserShell.window.close(); // { accepted, code, message }
```

### 导航约束

`AllowedOrigins` 为空表示不限制导航来源；非空时只允许列表内的 Origin（含
`NavigationStarting` 拦截与新窗口处理）。`NewWindowRequestedAsync` 为 null 时，
页面发起的新窗口请求会在当前窗口内导航。

## 宿主

`BrowserShell.WebView.Wpf` 是纯类库，不含程序入口。宿主进程由引用方自行编写：

```csharp
public partial class App : Application
{
    private WebViewShell? _shell;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _shell = await WebViewShell.CreateAsync(Dispatcher);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_shell is not null) await _shell.DisposeAsync();
        base.OnExit(e);
    }
}
```

宿主 WPF 应用需满足：`net8.0-windows`、启用 WPF、引用 `BrowserShell.WebView.Wpf`。
`WebViewWindow` 本身即 `ChromeWindow` 派生类，宿主也可直接继承它扩展自定义行为。

## 依赖方向

```
宿主 exe（引用方自建）
   ├──> BrowserShell.WebView.Wpf  (net8.0-windows)
   └──> BrowserShell.Service.SDK  (net8.0)

BrowserShell.WebView.Wpf ──> WindowChromeKit.Wpf + Microsoft.Web.WebView2
BrowserShell.Service.SDK ──> 不引用 WPF
```

`BrowserShell.Service.SDK` 保持 `net8.0` 且无 WPF 依赖，因此 ASP.NET Core 服务引用它不会引入桌面依赖链。

## 状态

迁移自 `SoftwareHub.DesktopAgent`（1.0.4），本仓库版本 `2.0.0`。

消费方集成层（原 `SoftwareHub.Service.SDK.Hosting/Desktop`）不在本仓库内：它是 SoftwareHub 对 BrowserShell 的适配层，需另行接入。
