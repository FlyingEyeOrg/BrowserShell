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
    TitleBarStyle = ChromeTitleBarStyle.VsCode,
});
```

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
