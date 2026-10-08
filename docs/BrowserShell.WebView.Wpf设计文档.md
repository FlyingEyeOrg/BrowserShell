# BrowserShell.WebView.Wpf 设计文档

状态：**设计稿**（待评审；评审通过后再开发）
目标版本：**3.0.0**（含破坏性 API 变更）
当前版本：2.0.0
最后核对：以当前 `HEAD` 源码为准，行数与调用点均经实际检索

---

## 一、定位

### 1.1 一句话定义

> **`BrowserShell.WebView.Wpf` 把一段 URL 变成一个可用的桌面窗口，并把窗口级操作反向暴露给页面。**

它只做"**承载**"与"**窗口**"两件事，不多做。

### 1.2 判断标准

任何能力要进本库，必须**同时**满足：

1. 与"承载网页 / 管理窗口"直接相关；
2. 对所有宿主都成立（不依赖具体业务）。

只满足其一的，一律留在宿主侧。

### 1.3 它不是什么

| 不是 | 说明 |
|---|---|
| 不是应用框架 | 不提供 DI、模块化、生命周期编排 |
| 不是 IPC / 进程间协议层 | 纯进程内类库，无远程会话、无认证 |
| 不是页面业务 SDK | 不碰登录、权限、业务弹窗 |
| 不是窗口边框库 | 原生边框/拖动/Snap/DWM 全部外包给 `WindowChromeKit` |
| **不是 WPF 窗口 API 的二次封装** | **宿主已能从 `ChromeWindow` 继承获得的能力，本库不重复提供** |

最后一条是本次设计的核心约束，它直接决定第四节要删除的 API。

### 1.4 与周边项目的边界

| 项目 | TFM | 关系 |
|---|---|---|
| `BrowserShell.WebView.Wpf` | `net8.0-windows` | 本库 |
| `BrowserShell.Service.SDK` | `net8.0` | **无代码依赖**，两者互不引用 |
| `BrowserShell.WebView.Wpf.Sample` | `net8.0-windows` | 本库示例宿主 |
| `BrowserShell.WebView.Wpf.Tests` | `net8.0-windows` | 本库单元测试 |

> `BrowserShell.Service.SDK` 描述的是"服务通过 SDK 打开桌面窗口"的**进程间**形态，
> 其运行时（Kestrel/SignalR 代理层）已在重构中整块删除。本库因此是**纯进程内类库**。
> 本文档中"宿主"一律指**引用本库的进程内宿主程序**。

---

## 二、职责边界

### 2.1 本库负责

| # | 职责 | 承载 |
|---|---|---|
| R1 | **窗口托管**：创建、按标识跟踪、查询、关闭、统一释放 | `WebViewShell` |
| R2 | **WebView2 环境**：进程内唯一环境、Profile 隔离、用户数据目录生命周期 | `WebViewWindowEnvironment`（内部） |
| R3 | **初始化时序与失败降级**：窗口先出来、失败可见、不闪退 | `WebViewWindow` |
| R4 | **导航**：首次导航、重新加载、同源约束、新窗口处理 | `WebViewWindow` |
| R5 | **加载呈现**：导航过程的加载指示与底色，避免白屏/白闪 | `WebViewPresentationMask` |
| R6 | **关闭裁决**：关闭请求交宿主判定并回传结果 | `WebViewWindow` |
| R7 | **页面侧控制桥**：注入 `window.browserShell.window.*` | `WebViewWindow` |

R1/R2 的初始化并发由 `WebView2InitializationCoordinator`（内部）串行化。

### 2.2 本库不负责

| 不负责 | 归属 |
|---|---|
| 原生窗口边框、拖动、缩放、Snap、DWM 阴影、DPI | `WindowChromeKit.Wpf` 的 `ChromeWindow` |
| 业务登录 / OAuth / Cookie / 权限模型 | 宿主或页面自身 |
| 页面内 UI（对话框、通知、进度条） | 宿主或页面自身 |
| 进程间协议、远程会话、认证 | 不在本库 |
| 多显示器布局策略（除"居中 / 约束到工作区"外） | 宿主 |
| DI 容器接入 | 宿主 |
| **对继承自 `ChromeWindow` 的窗口操作的再包装** | **宿主直接用继承来的 API** |

### 2.3 依赖

```
BrowserShell.WebView.Wpf  (net8.0-windows)
  ├── Microsoft.Web.WebView2      1.0.3537.50
  └── WindowChromeKit.Wpf         2.1.1
```

仅两个包。**不含** `Microsoft.AspNetCore.App`、SignalR、Serilog。

---

## 三、目标 API（3.0.0）

公开类型从当前 5 个收敛为 **4 个**，全部 `sealed`。

| 类型 | 角色 |
|---|---|
| `WebViewShell` | 入口：创建 / 跟踪 / 关闭窗口 |
| `WebViewWindow` | 窗口单元：单个 Web 外壳窗口 |
| `WebViewWindowOptions` | 一次打开的全部设置 |
| `WebViewWindowClosingContext` | 关闭裁决的输入 |

### 3.1 `WebViewShell`

```csharp
public sealed class WebViewShell : IAsyncDisposable
{
    public static Task<WebViewShell> CreateAsync(
        Dispatcher dispatcher,
        string? browserExecutableFolder = null,
        string? userDataFolder = null,
        CancellationToken token = default);

    public int WindowCount { get; }
    public IReadOnlyCollection<WebViewWindow> Windows { get; }
    public bool TryGetWindow(string id, out WebViewWindow? window);

    public Task<WebViewWindow> OpenAsync(
        WebViewWindowOptions options,
        string? id = null,
        CancellationToken token = default);

    public Task CloseAsync(string id, string source = "Host");
    public ValueTask DisposeAsync();
}
```

**约定与失败语义**

| 情形 | 行为 |
|---|---|
| `CreateAsync` 环境初始化失败（如未装 Runtime） | 抛异常 |
| `OpenAsync` 的 `id` 为 null | 自动生成 GUID（`N` 格式） |
| `OpenAsync` 的 `id` 为空白 | 抛 `ArgumentException` |
| `OpenAsync` 的 `id` 重复 | 抛 `InvalidOperationException` |
| `OpenAsync` 第二阶段（初始化/显示）失败 | 移除注册 + 释放 + 上抛，**不留空壳窗口** |
| `CloseAsync` 指定不存在的 `id` | 静默返回（幂等） |
| `CloseAsync` 被 `ClosingAsync` 拒绝 | **窗口保留在管理器中**，不视为已关闭 |
| `DisposeAsync` | 先释放全部窗口，再释放环境；并发调用只生效一次 |

**线程模型**

- 窗口 UI 操作必须在传入的 `Dispatcher` 上。WPF 的 `Window`（`Show`／`Hide`／`Close`）与
  `WebView2`（派生自 `HwndHost`）都是 `DispatcherObject`，其内部 `VerifyAccess()`
  会对错误线程抛 `InvalidOperationException`。
- 下列成员**自动封送回 UI 线程**，故可从任意线程调用（已在 UI 线程时同步直接执行）：
  `OpenAsync`、`CloseAsync`、`ClosePermanently`、`DisposeAsync`。
- `OpenAsync` 的**窗口构造、`InitializeAndShowAsync` 与失败回收路径**均在 UI 线程回调内完成。
  早期版本只把「构造」放进 `InvokeAsync`，导致从后台线程调用时
  `EnsureHandle()`／`Show()` 抛异常——这已修复。
- 注册表操作（增 / 删 / 清空 / **读**）**统一在 UI 线程执行**，非 UI 线程调用时自动切换。
  `WindowCount` / `Windows` / `TryGetWindow` 是**无封送的属性访问**，仍应在 UI 线程读取。
- `DisposeAsync` 与 `OpenAsync` 并发时，以 `Interlocked` 一次性转移保证只释放一次。

### 3.2 `WebViewWindow`

```csharp
public sealed class WebViewWindow : ChromeWindow, IAsyncDisposable
{
    public string Id { get; }
    public bool IsClosed { get; }
    public string? LastNavigationError { get; }

    public Task NavigateAsync(Uri target);   // 受 AllowedOrigins 约束
    public Task ReloadAsync();
    public Task CloseAsync(string source = "Host");
    public ValueTask DisposeAsync();
}
```

**设计要点**

- **不提供** `MinimizeAsync` / `MaximizeAsync` / `RestoreAsync` / `SetTitleAsync` /
  `ShowWindowAsync` / `HideAsync`。理由是它们只是转发到继承来的 `WindowState` / `Title` /
  `Show()` / `Hide()`（见 4.1）。
- `NavigateAsync` / `ReloadAsync` **保留**：它们是"套壳"的固有能力，且
  `NavigateAsync` 承担了 `AllowedOrigins` 校验（宿主无法从基类获得该约束）。
- `CloseAsync` 走 `ClosingAsync` 裁决；`ClosePermanently` **删除**（绕过裁决，与 R6 相悖）。
- `InitializeAndShowAsync` 与 `ApplyOptions` **收回 `internal`**：唯一调用方是
  `WebViewShell`，公开无意义。
- `LastNavigationError`：首次导航失败原因。**成功导航时重置为 null**（当前实现不重置，
  属待修，见 6.6）。
- `NavigateAsync` / `ReloadAsync` 当前**立即返回**（不等待导航完成）。3.0.0 保持这一语义，
  但在 XML 文档中明确写出，避免宿主误以为 `await` 即已加载完成。

### 3.3 `WebViewWindowOptions`

```csharp
public sealed class WebViewWindowOptions
{
    // —— 窗口 ——
    public Uri? Url { get; init; }
    public string? Title { get; init; }                      // 空则回退 Url.Host
    public double Width { get; init; } = 1024;
    public double Height { get; init; } = 768;
    public double MinWidth { get; init; }
    public double MinHeight { get; init; }
    public bool Topmost { get; init; }
    public bool Center { get; init; } = true;
    public bool Focus { get; init; } = true;
    public bool ShowInTaskbar { get; init; } = true;
    public bool Resizable { get; init; } = true;
    public ChromeTitleBarStyle TitleBarStyle { get; init; } = ChromeTitleBarStyle.Chrome;
    public ChromeTitleBarPalette TitleBarPalette { get; init; } = ChromeTitleBarPalette.Default;

    // —— 外观（3.0.0 新增）——
    public Color BackgroundColor { get; init; } = Colors.White;

    // —— 安全 ——
    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];   // 空 = 不限制

    // —— 回调 ——
    public Func<WebViewWindowClosingContext, CancellationToken, Task<bool>>? ClosingAsync { get; init; }
    public Func<WebViewWindow, Task>? OpenedAsync { get; init; }
    public Func<WebViewWindow, Task>? ClosedAsync { get; init; }
    public Func<WebViewWindow, Uri, Task>? NewWindowRequestedAsync { get; init; }
}
```

**`BackgroundColor` 是本次新增的唯一配置项**，用于消除深色页面的白闪（见 5.2 与 6.1）。
它同时作用于三处（当前三处都硬编码白色）：

| 作用点 | 当前值 | 3.0.0 |
|---|---|---|
| `WebView2.DefaultBackgroundColor` | `Color.White`（`WebViewWindow.cs:22`） | `BackgroundColor` |
| `_presentationRoot` 背景 | `Brushes.White`（`:29`） | `BackgroundColor` |
| 注入遮罩背景 | `'#ffffff'`（`WebViewPresentationMask.cs:25`） | `BackgroundColor`（脚本参数化） |

### 3.4 `WebViewWindowClosingContext`

```csharp
public sealed class WebViewWindowClosingContext
{
    public WebViewWindow Window { get; }
    public string Source { get; }   // "WindowChrome" | "Page" | 宿主自定义
}
```

### 3.5 不再公开的类型

| 类型 | 3.0.0 可见性 | 理由 |
|---|---|---|
| `WebViewWindowEnvironment` | **`internal`** | 见下 |
| `WebView2InitializationCoordinator` | `internal`（不变） | 实现细节 |
| `WebViewPresentationMask` | `internal`（不变） | 实现细节 |

**为什么 `WebViewWindowEnvironment` 必须收回**：它当前是 `public`，但
`WebViewShell` 的 `_environment` 字段是私有的且**没有任何公开访问器**，全库也没有任何
API 接受一个 `WebViewWindowEnvironment` 实例。宿主唯一能做的就是自己调
`WebViewWindowEnvironment.Create(...)` 造一个，然后**无处可用**。

也就是说，它是一个**公开的死胡同类型**——暴露了实现细节，却不提供任何用途。
收回 `internal` 不损失任何宿主能力（那两个目录参数已由 `WebViewShell.CreateAsync` 承载），
同时消除 `Core`（`CoreWebView2Environment`）与 `CreateControllerOptions` 的原生类型泄漏。

### 3.6 公开 API 一览（3.0.0 目标态）

```csharp
namespace BrowserShell.WebView.Wpf;

public sealed class WebViewShell : IAsyncDisposable
{
    public static Task<WebViewShell> CreateAsync(Dispatcher dispatcher,
        string? browserExecutableFolder = null, string? userDataFolder = null,
        CancellationToken token = default);
    public int WindowCount { get; }
    public IReadOnlyCollection<WebViewWindow> Windows { get; }
    public bool TryGetWindow(string id, out WebViewWindow? window);
    public Task<WebViewWindow> OpenAsync(WebViewWindowOptions options,
        string? id = null, CancellationToken token = default);
    public Task CloseAsync(string id, string source = "Host");
    public ValueTask DisposeAsync();
}

public sealed class WebViewWindow : ChromeWindow, IAsyncDisposable
{
    public string Id { get; }
    public bool IsClosed { get; }
    public string? LastNavigationError { get; }
    public Task NavigateAsync(Uri target);
    public Task ReloadAsync();
    public Task CloseAsync(string source = "Host");
    public ValueTask DisposeAsync();
}

public sealed class WebViewWindowOptions { /* 见 3.3 */ }

public sealed class WebViewWindowClosingContext
{
    public WebViewWindow Window { get; }
    public string Source { get; }
}
```

---

## 四、与 2.0.0 的差异

**本次为破坏性变更**，故版本升至 3.0.0。

### 4.1 删除：纯转发的窗口命令包装

`WebViewWindow : ChromeWindow : Window`，因此下列能力**宿主本来就能通过继承的 API 使用**：

| 删除的成员 | 方法体（当前实现） | 宿主改用 |
|---|---|---|
| `MinimizeAsync()` | `WindowState = WindowState.Minimized;` | `window.WindowState = ...` |
| `MaximizeAsync()` | `WindowState = WindowState.Maximized;` | 同上 |
| `SetTitleAsync(t)` | `Title = t ?? string.Empty;` | `window.Title = t` |
| `HideAsync()` | `Hide();` | `window.Hide()` |
| `ShowWindowAsync()` | `Show(); ConstrainToWorkArea(); Activate();` | `window.Show(); window.Activate();` |
| `RestoreAsync()` | `WindowState = Normal; ConstrainToWorkArea();` | 见 4.3（行为需先迁移） |

**规模**：6 个方法合计 **33 行方法体**（含 XML 注释 **39 行**）。

它们全仓**零调用**（示例与测试都不使用），且页面桥也**不经过**它们
（桥直接设 `WindowState`，见 `HandleBridgeMessageAsync`）。
保留它们只会让公开面看起来像 SDK，实际是噪音。

> 说明其中的**真实行为**（非纯转发）只有两处，删除时必须迁移：
> `ShowWindowAsync` 的 `Activate()` 与 `ConstrainToWorkArea()`、
> `RestoreAsync` 的 `ConstrainToWorkArea()`。详见 4.3。

### 4.2 删除：与职责冲突或语义冗余

| 成员 | 处理 | 理由 |
|---|---|---|
| `ClosePermanently()` | 删除 | 绕过关闭裁决，与 R6 直接冲突 |
| `ApplyOptions(options)` | 收回 `internal` | 仅构造函数内部调用一次 |
| `InitializeAndShowAsync(token)` | 收回 `internal` | 唯一调用方是 `WebViewShell.OpenAsync` |
| `WebViewPresentationMask.PostVisibility(...)` | 删除 | 0 调用点（实际走 `SetVisibilityAsync`） |

### 4.3 保留但需说明

| 成员 | 处理 | 理由 |
|---|---|---|
| `WindowCount` / `Windows` / `TryGetWindow` | **保留** | R1"窗口托管"的自然组成；多窗口场景按 id 找回窗口是基础能力 |
| `IsClosed` | **保留** | 宿主在回调中判断窗口状态的最低需要 |
| `NavigateAsync` / `ReloadAsync` | **保留** | 套壳固有能力；前者含同源校验 |

**待迁移的真实行为**：4.1 的 6 个包装中，两处含非转发逻辑，删除前必须落位：

| 来源 | 行为 | 3.0.0 去向 |
|---|---|---|
| `ShowWindowAsync` | `ConstrainToWorkArea()` + `Activate()` | 窗口显示流程内部已做约束；`Activate` 由 `options.Focus` 控制 |
| `RestoreAsync` | `ConstrainToWorkArea()` | **必须迁移**，见下 |

> **`RestoreAsync` 的额外行为需要迁移，不是自动保留**。已核实：
>
> | 路径 | 设 `WindowState = Normal` | 调 `ConstrainToWorkArea` |
> |---|---|---|
> | `RestoreAsync()`（将删除） | ✅ | ✅ |
> | 页面桥 `case "restore"` | ✅ | ❌ **没有** |
> | `OnDisplayConfigurationChanged` | ❌ | ✅（仅显示器变化时） |
>
> 也就是说，`RestoreAsync` 是**当前唯一**在"还原"时把窗口拉回工作区的路径。删除它之前，
> 该行为必须迁移到内部方法，并让页面桥的 `restore` 分支也调用它——否则会出现
> **行为倒退**：窗口从最小化还原到一台已被拔掉的显示器上时，会留在屏幕外。
>
> 这同时修掉 2.0.0 的一处不一致（两条 restore 路径行为不同）。

### 4.4 新增

| 项 | 说明 |
|---|---|
| `WebViewWindowOptions.BackgroundColor` | 贯通三处底色，消除深色页面白闪 |
| `WebViewWindowEnvironment` 改 `internal` | 移除公开死胡同类型 |
| 遮罩全局名重命名 | `__softwarehub_*` → `__browserShell*`（见 6.5） |

### 4.5 变更汇总

| 动作 | 数量 |
|---|---|
| 删除公开成员 | **7**（6 个包装 + `ClosePermanently`） |
| 收回 `internal` | **3**（`ApplyOptions`、`InitializeAndShowAsync`、`WebViewWindowEnvironment`） |
| 新增公开成员 | **1**（`BackgroundColor`） |
| 净减代码 | 约 **41 行**（33 包装体 + 2 `ClosePermanently` + 6 `PostVisibility`） |

> 净减口径为**实际删除的方法体行数**，不含 XML 注释（含注释时包装部分为 39 行）。
> `ApplyOptions` / `InitializeAndShowAsync` / `WebViewWindowEnvironment` 仅改可见性，
> **不减少行数**，故不计入净减。

---

## 五、关键机制

### 5.1 打开窗口时序

```
宿主  WebViewShell.CreateAsync(dispatcher)
        └─ WebViewWindowEnvironment.Create(...)   准备临时用户数据目录
           └─ InitializeAsync()                    CoreWebView2Environment.CreateAsync

宿主  shell.OpenAsync(options, id)
        ├─ [UI 线程] 校验未释放 → 校验 id → 查重 → 构造窗口 → 注册（Add）
        └─ window.InitializeAndShowAsync(token)
              ├─ EnsureHandle()        必须先有 HWND，再创建 controller
              ├─ Show() + 居中 / 约束到工作区
              ├─ 等待 Loaded / Render 排空
              └─ InitializeWebViewAsync(token)
                    ├─ [串行] EnsureCoreWebView2Async
                    ├─ 收紧安全设置
                    ├─ 注入遮罩脚本 + 页面桥脚本
                    ├─ 首次导航（30s 超时；失败不抛，写入 LastNavigationError）
                    └─ Focus 时 Activate()
        └─ OpenedAsync 回调
```

**关键约束**：`EnsureHandle()` 必须在创建 WebView2 controller **之前**，否则自绘标题栏会
落在 DWM 尚未接管客户区的时间点，出现绘制错位。

**失败隔离**：窗口显示与 WebView2 初始化是两段独立兜底。窗口先出来；WebView 失败时保留
窗口并显示原因，避免"一闪即消"。

### 5.2 加载呈现（3.0.0 重新设计）

**现状问题**：库里存在**两套加载指示**，文案相同（"正在加载…"），且底色三处硬编码白色：

| # | 机制 | 生效时机 | 底色 |
|---|---|---|---|
| A | WPF `Border` + `ProgressBar`（`_initializationSurface`） | 仅首次加载（WebView 此时 `Hidden`） | `Brushes.White`（`:593`） |
| B | 注入页面的 DOM 遮罩（`WebViewPresentationMask`） | 首次加载**之后**的每次导航 | `'#ffffff'`（`:25`） |

之所以存在两套，是因为**空域（airspace）限制**：`WebView2` 承载在原生 HWND 上，
WPF 元素无法覆盖它。故 A 只能在"WebView 隐藏"时使用，B 则把遮罩注入页面 DOM 内部
（`position: fixed` + `z-index: 2147483647` + closed shadow root）以绕开该限制。

**3.0.0 方案：统一为 B 一套机制 + 底色贯通**

前置事实（已核实，决定了改法的边界）：

- `_webView` 初值是 `Visibility.Hidden`（`:27`），直到**首次导航完成后**才在 `:216`
  变为 `Visible`。
- 因此**首次加载全程 WebView 都是隐藏的**，注入的遮罩虽然在文档里，用户却看不到它；
  此时用户看到的是 WPF 层 A。
- 也就是说，A 存在的**真正原因**不是空域（此阶段 WebView 隐藏，无冲突），
  而是"**WebView 未显示时需要一个加载指示**"。

因此"统一为 B"**必须附带一个改动**：让 `_webView` 从一开始就 `Visible`
（WebView2 在无内容时显示 `DefaultBackgroundColor`，配合 3.3 的 `BackgroundColor` 即为纯色），
A 才能真正退场。

方案步骤：

1. `WebView2.DefaultBackgroundColor`、`_presentationRoot.Background`、注入遮罩背景
   全部取 `options.BackgroundColor`；
2. `_webView` 初始即 `Visible`（不再全程 `Hidden`），加载指示统一由注入遮罩承担；
3. WPF 层 `Border` **只保留失败面板**用途——WebView2 初始化失败时展示原因。

**收益**：消除重复机制、消除三处硬编码白色、消除白闪。
**代价**：
- 从窗口出现到 core 就绪之间（数百毫秒）只有 `BackgroundColor` 纯色、**无转圈**；
- `_webView` 提前可见后，若 core 初始化失败，失败面板需要像现在这样**覆盖**住 WebView
  （现实现为 `_webView.Visibility = Collapsed`，该路径保留）。

> **待确认（决策点 D-A）**：是否接受"core 就绪前无 spinner"以换取机制统一？
> 保守替代：保留 A、仅把其底色改为 `BackgroundColor`——两套机制并存但不再白闪。
> **本文档推荐统一方案**，因"两套文案相同的加载指示"本身是维护负担。

### 5.3 页面侧控制桥

注入 `globalThis.browserShell.window`（冻结对象）：

| 成员 | 行为 |
|---|---|
| `windowId` | 当前窗口标识 |
| `minimize()` / `maximize()` / `restore()` | 单向，无回执 |
| `close()` | 返回 `Promise<{accepted, code, message}>`，经 `requestId` 关联回执 |

消息往返：

```
页面 → 宿主   { type:'shellWindow', operation, value, requestId }
              → core.WebMessageReceived → HandleBridgeMessageAsync
宿主 → 页面   { type:'shellWindowCloseResult', requestId, accepted, code, message }
              → PostWebMessageAsJson
```

**约束**：`WebMessageReceived` 仅由**顶层文档**触发。iframe 内的页面**拿不到此桥**。

### 5.4 关闭裁决

```
来源 A  标题栏关闭按钮   → WPF Closing 事件
来源 B  页面 browserShell.window.close()  → WebMessage
来源 C  宿主 window.CloseAsync(source) / shell.CloseAsync(id, source)

        ClosingAsync == null  → 直接关闭
        否则 → 请求裁决
                 ├─ 已有裁决在途 → 回 CLOSE_PENDING（来源 B 专用）
                 ├─ accept → CloseCore()
                 └─ reject → 保持打开
```

- 标题栏关闭用 `eventArgs.Cancel = true` 拦截后转入裁决；`CloseCore()` 用 `_forceClose`
  放行真正的 `Close()`，避免二次裁决死循环。
- `CloseCore()` 先 `Hide()` 再 `Close()`，并清空 `Owner`。
- 裁决回调抛异常时按**拒绝**处理（来源 B 回传 `HANDLER_FAILED`）。
- 窗口**真正关闭后**回调所有者移出注册表，且**先移出再回调 `ClosedAsync`**，
  以保证 `ClosedAsync` 中读到的 `WindowCount` 已更新。

### 5.5 同源约束

`AllowedOrigins` 为空 = 不限制；非空时同时作用于：

- `NavigationStarting` —— 拦截跨 Origin 导航；
- `NewWindowRequested` —— 跨 Origin 的新窗口请求被忽略。

`NewWindowRequestedAsync` 为 null 时，页面发起的新窗口请求在**当前窗口内导航**。

---

## 六、必须修复的缺陷（开发任务）

按优先级排序。每项都已在源码中核实，并给出验收标准。

### 6.1 深色页面白闪（体验缺陷）

**现象**：深色页面点"重新加载当前页"时闪一下白屏。已在 `demo.html` + headless Edge 复现。

**成因**：遮罩脚本以 `let visible = true` 立即立起，且背景硬编码 `#ffffff`；
揭罩发生在 `NavigationCompleted`（即 `body.onload`），故白遮罩覆盖**整个加载期**。

**修复**：`BackgroundColor` 贯通三处（见 3.3 / 5.2）。

**验收**：示例页设 `BackgroundColor = #1b1b1f`（与 `demo.html` 一致）后，
反复点"重新加载当前页"**无白闪**。

### 6.2 导航失败时遮罩不揭，页面永久点不动（严重）

**成因**：后续导航的揭罩条件带了 `IsSuccess`：

```csharp
if (initialPresentationCompleted && eventArgs.IsSuccess)   // WebViewWindow.cs:175
    _ = RevealCompletedNavigationAsync(core);
```

遮罩 `pointerEvents: 'auto'` 且全屏。**加载失败（断网 / 404 / 超时）时揭罩被跳过**，
窗口永久停在白色转圈界面且鼠标点不进去。对照首次加载的揭罩（`:213`）是**无条件**的。

**修复**：无论成败都揭罩（失败时页面本就是错误页，也应可见）。
并保留现有 `catch { }` 行为，遮罩操作失败不得阻断导航。

**验收**：对不可达地址执行导航（如断开网络后点链接），窗口在超时后显示错误页且**可交互**，
不停留于遮罩。

### 6.3 页面桥消息解析无防护（严重）

**成因**：`WebMessageReceived` 以 fire-and-forget 接收，而 handler 无 try/catch：

```csharp
core.WebMessageReceived += (_, e) => _ = HandleBridgeMessageAsync(e.WebMessageAsJson);  // :169
```

`HandleBridgeMessageAsync` 首行即 `JsonDocument.Parse` + `TryGetProperty`。而
`JsonElement.TryGetProperty` 在 `ValueKind != Object` 时**抛 `InvalidOperationException`**。
页面可发送**任意 JSON 值**：

```js
chrome.webview.postMessage(42);      // → "42"      → ValueKind = Number → 抛异常
chrome.webview.postMessage(null);    // → "null"    → ValueKind = Null   → 抛异常
```

（已用 Roslyn/JSON 实测确认该异常行为。）异常成为 **unobserved task exception**。
注意 `AreHostObjectsAllowed = false` **挡不住**这条通道——它是宿主通信通道，不属于 host objects。

**修复**：解析前判断 `ValueKind == JsonValueKind.Object`，并给整个方法加 try/catch。

**验收**：页面执行 `chrome.webview.postMessage(42)` / `null` / 字符串 / 数组后，
宿主不产生任何未观察异常，窗口功能正常。

### 6.4 `close()` 回执可能永久挂起（健壮性）

**成因**：页面侧 `closeRequests` 是 `Map`，**无超时**。若宿主未回执
（如裁决中崩溃，或 `PostCloseResult` 因 `CoreWebView2` 为 null 而静默跳过），
`await browserShell.window.close()` **永久挂起**，Map 条目亦不释放。

**修复**：页面侧 `close()` 增加超时（建议 5s），超时返回
`{ accepted:false, code:'TIMEOUT', message:'宿主未响应。' }` 并清理 Map。

**验收**：模拟宿主不回执，`await close()` 在超时后兑现且不泄漏条目。

### 6.5 注入脚本品牌残留

遮罩脚本内仍是 `SoftwareHub` 标识：`WebViewPresentationMask.cs:11`（`__softwarehub_presentation_mask__`）、
`:66`、`:93`（`__softwareHubSetPresentationMask`）。迁移报告"残留 = 0"只统计了 C# 标识符，
**未覆盖内嵌 JS 字符串**。

**修复**：重命名为 `__browserShellPresentationMask` / `__browserShellSetPresentationMask`。
该全局名未对外文档化，且 3.0.0 本就是破坏性版本，**现在是改名成本最低的时机**。

### 6.6 `LastNavigationError` 不重置（一致性）

成功导航后该属性保留上一次的失败原因，宿主无法据此判断"当前是否正常"。

**修复**：导航开始或成功时重置为 null。

### 6.7 桥未校验消息来源（安全，视场景）

`WebMessageReceived` 未检查 `eventArgs.Source`（发送方文档 URI）。
当前只校验 `type == 'shellWindow'`。若宿主承载**非受信页面**，该页面也能调
`close()` / `minimize()` 操作自身窗口，并触发宿主的 `ClosingAsync` 裁决。

**处理**：**待确认（决策点 D-B）**——宿主是否可能承载第三方页面？
- 若仅承载自有页面：不处理，保持简单；
- 若可能承载第三方：用 `eventArgs.Source` 做来源校验。

### 6.8 线程封送缺失（严重，已修复）

**现象**：文档承诺 `OpenAsync`「内部切回 UI 线程，故可从任意线程调用」，
但 `InvokeAsync` 只包住了窗口**构造**，`InitializeAndShowAsync` 在闭包**外**执行。
而它的第一步就是 `EnsureHandle()` / `Show()`，二者均经
`DispatcherObject.VerifyAccess()` 校验线程（WPF 源码已核实：`Window.Show/Hide/Close`
与 `HwndHost.Dispose(bool)` 都调用 `VerifyAccess()`，失败抛 `InvalidOperationException`）。

**后果**：从后台线程调 `OpenAsync` 会在 `EnsureHandle()` 抛异常；
`CloseAsync` / `DisposeAsync` 同样无法按文档所述从任意线程调用。

**修复**（已实施）：
- 新增 `MarshalAsync` / `Marshal`（无条件封送）与 `RunOnUiThreadAsync` / `RunOnUiThread`
  （封送，且已关闭时跳过），区分「公开成员」与「关闭清理」两类语义。
- `InitializeAndShowAsync`、`CloseAsync`、`ClosePermanently`、`DisposeAsync` 内部自动封送。
- `DisposeWebViewOnce` 用**无条件**封送：释放恰在窗口已关闭时最需要执行，
  若沿用「已关闭则跳过」的守卫会造成 WebView2 泄漏。
- `WebViewShell.CloseAsync` / `DisposeAsync` 的注册表**读取**也切回 UI 线程
  （`Dictionary` 非线程安全）。

**同批修复的并发问题**：
- `NotifyClosedOnceAsync` 原为「检查再赋值」，两条触发路径（`Closed` 事件与
  `DisposeAsync`）并发时可能重复触发宿主的 `ClosedAsync`；改为复用同一个通知 Task。
- 由此消除了「`DisposeAsync` 在宿主 `ClosedAsync` 尚未跑完时即返回」的窗口。
- 引入重入保护：宿主在 `ClosedAsync` 内调 `DisposeAsync` 时不得等待该回调自身的通知，
  否则自锁（该场景已用可复现的并发测试验证：旧写法死锁、新写法通过）。

**验收**：从后台线程调用 `OpenAsync` / `CloseAsync` / `DisposeAsync` 均不抛
`InvalidOperationException`；并发 8 路 `DisposeAsync` 时宿主 `ClosedAsync` 恰好回调一次。

---

## 七、非目标（明确不做）

以下能力**明确不在 3.0.0 范围**，如需应另立项目或由宿主实现：

| 非目标 | 理由 |
|---|---|
| 跨平台（macOS / Linux） | WPF 限定 Windows；跨平台应评估其他技术栈 |
| 合成式承载（`CoreWebView2CompositionController`） | 可消除空域问题，但需自行实现输入/焦点/DPI/无障碍，成本远超"基础套壳" |
| 进程间协议 / 远程会话 | 已删除，不恢复 |
| 页面业务登录、权限模型 | 属宿主 |
| 模态子窗口输入门控 | 已删除（无生产调用点），不恢复 |
| 多窗口布局策略（平铺 / 层叠 / 记忆位置） | 属宿主 |
| DI 容器集成 | 属宿主 |
| 安全开关的开放（DevTools / 下载 / 权限放开） | 保持"默认全部拒绝"；如需放开另议 |

---

## 八、验收标准

3.0.0 交付需**同时**满足：

| # | 标准 |
|---|---|
| 1 | `dotnet build -c Release` **0 警告 0 错误** |
| 2 | `dotnet test` 全绿（须在 Windows 上执行） |
| 3 | 公开类型恰为 **4 个**，且 `WebViewWindowEnvironment` 不再可见 |
| 4 | 公开成员中**无**纯转发包装（4.1 清单全部移除） |
| 5 | 6.1–6.6 全部修复并有对应测试或可复现验证步骤 |
| 6 | 示例应用可运行，且 `BackgroundColor` 设为深色时**无白闪** |
| 7 | 设计文档与本实现一致（含 3.0.0 差异表） |
| 8 | 破坏性变更已在 `README.md` 与示例中同步 |

**测试补齐**（当前仅 1 文件 / 24 行，有效覆盖率接近零）：

| 优先 | 目标 | 说明 |
|---|---|---|
| 高 | `WebViewShell` 窗口托管 | 唯一性、拒绝关闭后仍可查、自发关闭后移除、并发 `OpenAsync` |
| 高 | 关闭裁决矩阵 | 三来源 × 接受/拒绝/异常/在途 |
| 中 | 桥消息解析 | 6.3 的非法载荷用例（纯逻辑，无需真实 WebView2） |
| 中 | `WebView2InitializationCoordinator` | 串行性与取消（纯逻辑，无需真实 WebView2） |
| 低 | 环境与 Profile 隔离 | 需真实 WebView2 Runtime |

---

## 九、附录：现状审计依据

本节保留 2.0.0 的体检结论，作为第三节设计决策的依据。

### 9.1 规模

| 文件 | 行数 |
|---|---|
| `WebViewWindow.cs` | 595（占全库 54%） |
| `WebViewShell.cs` | 179 |
| `WebViewWindowEnvironment.cs` | 122 |
| `WebViewPresentationMask.cs` | 97 |
| `WebViewWindowOptions.cs` | 82 |
| `WebView2InitializationCoordinator.cs` | 28 |
| **合计** | **1103** |

`WebViewWindow.cs` 单文件承担 6 类关注点（外观、时序、装配、失败呈现、关闭裁决、注入脚本），
是可分离的。3.0.0 删除 4.1/4.2 内容后会缩小，进一步拆分留待实现阶段按需进行。

### 9.2 公开成员调用情况（2.0.0）

| 成员 | 仓内调用 |
|---|---|
| `WebViewShell.CreateAsync` / `OpenAsync` / `CloseAsync` / `DisposeAsync` | 有（示例、测试） |
| `WebViewWindow.CloseAsync` | 有（示例） |
| `MinimizeAsync` / `MaximizeAsync` / `RestoreAsync` / `SetTitleAsync` / `ShowWindowAsync` / `HideAsync` | **0** |
| `NavigateAsync` / `ReloadAsync` / `ApplyOptions` / `ClosePermanently` / `IsClosed` | **0** |
| `WindowCount` / `Windows` / `TryGetWindow` | **0** |
| `WebViewWindowEnvironment.Core` / `CreateControllerOptions` | **0**（且无处可用） |

### 9.3 安全默认值（保持不变）

`InitializeWebViewAsync` 中显式收紧，**默认拒绝**：

- `AreHostObjectsAllowed = false`
- `AreDefaultScriptDialogsEnabled = false`
- `AreDevToolsEnabled = false`
- `AreDefaultContextMenusEnabled = false`
- `IsStatusBarEnabled = false`
- `PermissionRequested` 一律 **Deny**
- `DownloadStarting` 一律 **Cancel**

（`IsWebMessageEnabled` **不设置**，取默认 `true`——这是页面桥能工作的前提，属正确的沉默。）

### 9.4 已修复项（2.0.0 期间）

| 项 | 结论 |
|---|---|
| 模态输入门控三件套（152 行 + 166 行测试） | 无生产调用点，已删除 |
| 窗口 ID 唯一性 | 查重与注册已并入同一 UI 线程回调，改用 `Add` |
| 关闭被拒后仍移出注册表 | 改为由真正关闭时的回调移除 |
| 自发关闭不通知 shell | 已由窗口关闭回调覆盖三来源 |
| 目录结构 | `Windows/` 空壳分层已拍平 |

### 9.5 当前测试

| 测试文件 | 行数 |
|---|---|
| `WebViewPresentationMaskTests.cs` | 24 |

`dotnet test` 依赖 `Microsoft.WindowsDesktop.App`，**须在 Windows 上运行**。

---

## 十、待确认决策点

| 编号 | 决策点 | 选项 |
|---|---|---|
| **D-A** | 加载指示是否统一为注入遮罩一套机制 | 统一（推荐，消除重复）/ 保留 WPF 面板但改其底色 |
| **D-B** | 桥是否校验 `eventArgs.Source` | 承载第三方页面则校验 / 仅自有页面则不校验 |
| **D-C** | `WindowCount` / `Windows` / `TryGetWindow` 是否保留 | 保留（推荐，R1 自然组成）/ 极简则删 |
| **D-D** | 版本号是否采用 3.0.0 | 采用（破坏性变更）/ 其它 |

---

## 相关文档

- [`../README.md`](../README.md) —— 仓库总览
- [`迁移报告.md`](迁移报告.md) —— 从 `SoftwareHub.DesktopAgent` 迁入与瘦身的完整记录
- [`../src/BrowserShell.WebView.Wpf/README.md`](../src/BrowserShell.WebView.Wpf/README.md) —— 面向使用者的快速开始
