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
| `WebViewWindowEnvironment` | 底层 WebView2 环境与隔离 Profile |

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
