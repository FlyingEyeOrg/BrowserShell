# BrowserShell

Windows 桌面 Web 外壳：把 Web 页面承载到 WPF 窗口。

本仓库由 SoftwareHub 的 DesktopAgent 迁移而来，独立于 SoftwareHub 发布。

## 组成

| 项目 | TFM | 说明 |
|---|---|---|
| `src/BrowserShell.WebView.Wpf` | `net8.0-windows` | 桌面 Web 外壳类库。在 `ChromeWindow` 上承载 WebView2 |
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

### 窗口定位

`Left` / `Top`（设备无关像素，虚拟桌面坐标）在**创建时**确定窗口首次出现的位置：

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
- 指定了显式坐标时 `Center` 被忽略，且**不做工作区裁剪**——坐标按原值使用，由调用方负责合法性
  （多显示器负坐标因此可用）
- 两个属性默认 `null`，行为与未提供时完全一致

> **为什么必须在创建时传**：`OpenAsync` 是"创建 + 首次导航 + 显示"一体，返回时窗口已经在屏幕上。
> 在它返回后再设 `window.Left` / `window.Top`，用户会先看到窗口出现在一处、再跳到目标位置
> （实测层叠偏移场景位移可达 dx=510 dy=159）。`Left` / `Top` 在显示之前应用，因此没有跳动。

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
   └──> BrowserShell.WebView.Wpf  (net8.0-windows)

BrowserShell.WebView.Wpf ──> WindowChromeKit.Wpf + Microsoft.Web.WebView2
```

本库是**纯进程内类库**：不含任何进程间协议、代理层或服务端连接组件。

## NuGet 自动发布

仓库默认不在本地跑 CI；发布工作流会在打包前执行 Release 构建与全部测试，避免发布无效包。

发布 `BrowserShell.WebView.Wpf`（`net8.0-windows`），同时产出 `.snupkg` 符号包。

自动发布使用 **NuGet.org Trusted Publishing**（OIDC）。NuGet Policy 中的仓库、
`publish-nuget.yml` 工作流和 `production` environment 必须与本仓库一致；
**不需要配置永久的 `NUGET_API_KEY` secret**。推送 `v` 开头的 SemVer 标签即可发布：

```powershell
git tag v2.4.0
git push origin v2.4.0
```

也可在 Actions 页面手动运行该工作流，并可填写 `package_version` 覆盖版本号
（留空时使用 csproj 中的 `Version`）。tag 只作为触发条件、不决定版本号；
已存在的版本由 `--skip-duplicate` 安全跳过。

## 状态

迁移自 `SoftwareHub.DesktopAgent`（1.0.4）。已在 NuGet.org 发布 `BrowserShell.WebView.Wpf`，
当前版本 **3.1.0-rc.1**。

| 版本 | 内容 |
|---|---|
| 2.1.0 | 新增 `WebViewWindowOptions.Left` / `Top`，创建时即确定窗口位置（向后兼容） |
| 2.1.1–2.1.2 | 修复页面含悬挂子资源时遮罩永久停留；改为在 `DOMContentLoaded` 揭罩 |
| 2.1.3 | 遮罩脚本不再注入子 iframe（修复页面内 iframe 永久「正在加载…」） |
| 2.2.0 | `OpenAsync` 默认不再等待首次导航结束（`WaitForFirstScreen`） |
| 2.3.0 | 用户数据目录跨进程复用，消除 WebView2 每次新建目录导致的 5 秒空转 |
| 3.0.0-rc.1 | **破坏性变更**：缓存迁到 `%LOCALAPPDATA%`、共用 profile、新增 Options 与 DI |
| 3.0.1 | 打开窗口后键盘焦点直接落到页面（新增 `FocusPage`） |
| 3.0.2 | `WindowChromeKit.Wpf` 升级到 2.3.0 |
| **3.1.0-rc.1** | `WindowChromeKit.Wpf` 升级到 2.3.1（预发布） |

3.0.0-rc.1 的改动：

- **用户数据目录迁到 `%LOCALAPPDATA%`**（原 `%TEMP%`）。原位置会被磁盘清理删除，
  且其 ACL 允许本机其他用户写入——里面存的是 Cookie 等登录数据。
  默认路径为 `%LOCALAPPDATA%\BrowserShell\{应用名}`，**不迁移旧缓存**。
- **所有窗口共用默认 profile**：磁盘占用不再随开窗次数增长，与浏览器模型一致。
- **新增 `WebViewShellOptions`**：根位置 / 产品目录 / 应用目录 / 完整路径四级配置。
- **新增 ASP.NET Core 风格 DI**：`services.AddBrowserShellWebView(...)`，
  提供 `WebViewShell` 单例与 `IWebViewShellFactory`。
- `WindowChromeKit.Wpf` 升级到 2.2.0（3.0.2 起 2.3.0，3.1.0-rc.1 起 2.3.1）。
- 源文件按职责分层（`Hosting` / `Environment` / `Windows` / `Presentation` / `Internal`），
  命名空间不变，调用方无需调整 `using`。

3.0.1 的改动：

- **打开窗口后键盘焦点直接落到页面**。此前只调 `Window.Activate()`，焦点停在窗口本身，
  用户看到窗口在前台但直接打字没有反应，须先点一下页面。
  新增 `FocusPage`（默认 `true`）与 `Focus` 分离：纯展示页或宿主想自行聚焦时可关掉。

消费方集成层（原 `SoftwareHub.Service.SDK.Hosting/Desktop`）不在本仓库内：它是 SoftwareHub 对 BrowserShell 的适配层，需另行接入。

原 `src/BrowserShell.Service.SDK`（服务侧窗口能力 SDK，`net8.0`）**已删除**：
它与本库无代码依赖，其运行时（Kestrel/SignalR 代理层）在更早的重构中已整块移除，
保留它只会让"BrowserShell 是什么"变得含混。迁移过程见 [`docs/迁移报告.md`](docs/迁移报告.md)。


