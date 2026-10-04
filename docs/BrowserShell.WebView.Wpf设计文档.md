# BrowserShell.WebView.Wpf 设计文档

状态：**讨论稿**（职责边界与待决事项尚未定案，见第七、八节）
适用版本：`2.0.0`
最后核对：以当前 `HEAD` 源码为准，行数与调用点均经实际检索

> 本版已落实一项决定：**删除无生产调用点的输入门控三件套**（见 7.1），
> 库规模由 9 文件 / 1211 行降至 **6 文件 / 1044 行**。

---

## 一、定位

`BrowserShell.WebView.Wpf` 是一个 **Windows 桌面 Web 套壳类库**：把 WebView2 承载到一个
原生 WPF 窗口里，并把它包装成"可被宿主程序批量打开、跟踪、关闭"的窗口单元。

一句话概括职责：

> **它负责"把网页变成一个像样的桌面窗口"，不负责"这个窗口是什么业务"。**

它**不是**应用框架，**不是** IPC/进程间协议层，**不是**页面业务 SDK。判断一个能力该不该
进这个库，可以用一条标准：

> 该能力是否与"承载网页并管理窗口"直接相关，且对所有宿主都成立？

同时满足才进；只对部分宿主成立的（业务登录、权限、页面对话框）留在宿主侧。

### 与周边项目的边界

| 项目 | TFM | 与本库的关系 |
|---|---|---|
| `BrowserShell.WebView.Wpf` | `net8.0-windows` | 本库 |
| `BrowserShell.Service.SDK` | `net8.0` | **无代码依赖**。两者当前互不引用 |
| `BrowserShell.WebView.Wpf.Sample` | `net8.0-windows` | 本库的可运行示例宿主 |
| `BrowserShell.WebView.Wpf.Tests` | `net8.0-windows` | 本库单元测试 |

> 注意：`BrowserShell.Service.SDK` 描述的是"服务通过 SDK 打开桌面窗口"的**进程间**形态，
> 那套运行时（Kestrel/SignalR 代理层）已在重构中整块删除（见 `迁移报告.md` 第七节）。
> 因此本库当前是一个**纯进程内类库**，没有任何远程会话、协议或认证概念。文档中如出现
> "服务"字样，均指**宿主进程**，不是远端服务。

---

## 二、职责划分

### 2.1 本库负责

| # | 职责 | 承载类型 |
|---|---|---|
| R1 | **窗口托管**：创建、按标识跟踪、查询、关闭、统一释放 | `WebViewShell` |
| R2 | **WebView2 环境**：进程内唯一环境、Profile 隔离、用户数据目录生命周期 | `WebViewWindowEnvironment` |
| R3 | **串行初始化**：避免多个窗口同时进入原生初始化 | `WebView2InitializationCoordinator` |
| R4 | **初始化时序与降级**：先出窗口后出内容、失败时保留窗口并显示原因 | `WebViewWindow` |
| R5 | **导航**：首次导航、重新加载、同源约束、新窗口处理 | `WebViewWindow` |
| R6 | **窗口级命令**：显示/隐藏/最小化/最大化/还原/标题/激活 | `WebViewWindow` |
| R7 | **关闭裁决**：把关闭请求交给宿主判定，并回传结果 | `WebViewWindow` + `WebViewWindowClosingContext` |
| R8 | **页面侧控制桥**：注入 `window.browserShell.window.*` | `WebViewWindow` |
| R9 | **加载遮罩**：导航过程遮盖白屏与闪烁 | `WebViewPresentationMask` |

### 2.2 本库明确不负责

| 不负责 | 归属 |
|---|---|
| 原生窗口边框、拖动、缩放、Snap Layout、DWM 阴影、DPI、工作区约束 | `WindowChromeKit.Wpf` 的 `ChromeWindow` |
| 业务登录、OAuth、Cookie、权限模型 | 宿主 / 页面自身 |
| 页面内 UI（对话框、通知、进度） | 宿主 / 页面自身 |
| 进程间协议、远程会话、认证、快照同步 | 不在本库；SDK 侧历史实现已移除 |
| 多显示器布局策略（除"居中/约束到工作区"外） | 宿主 |
| 模块化/DI 容器接入 | 宿主 |

### 2.3 依赖

```
BrowserShell.WebView.Wpf  (net8.0-windows)
  ├── Microsoft.Web.WebView2      1.0.3537.50   （WebView2 承载）
  └── WindowChromeKit.Wpf         2.1.1         （原生窗口边框与标题栏）
```

依赖收敛到两个包，且**不含** `Microsoft.AspNetCore.App`、SignalR、Serilog（重构时已移除）。

---

## 三、公开 API 契约

公开类型共 **5 个**，全部 `sealed`：

| 类型 | 角色 | 说明 |
|---|---|---|
| `WebViewShell` | 入口 | 外壳管理器：创建/跟踪/关闭窗口，持有进程内唯一 WebView2 环境 |
| `WebViewWindow` | 窗口单元 | 单个外壳窗口，派生自 `ChromeWindow` |
| `WebViewWindowOptions` | 配置 | 一次打开的完整设置 |
| `WebViewWindowClosingContext` | 回调上下文 | 关闭裁决的输入 |
| `WebViewWindowEnvironment` | 环境 | 底层 WebView2 环境与隔离 Profile |

### 3.1 `WebViewShell`

| 成员 | 签名要点 |
|---|---|
| `CreateAsync` | `(Dispatcher, string? browserExecutableFolder, string? userDataFolder, CancellationToken) → Task<WebViewShell>` |
| `OpenAsync` | `(WebViewWindowOptions, string? id, CancellationToken) → Task<WebViewWindow>` |
| `CloseAsync` | `(string id, string source = "Service") → Task` |
| `WindowCount` | `int` |
| `Windows` | `IReadOnlyCollection<WebViewWindow>` |
| `TryGetWindow` | `(string id, out WebViewWindow?) → bool` |
| `DisposeAsync` | `ValueTask` |

约定与失败语义：

- `CreateAsync` 会初始化进程内唯一的 WebView2 环境；环境创建失败（未安装 Runtime 等）时抛异常。
- `OpenAsync` 的 `id` 为 null 时自动生成 GUID（`N` 格式）。**重复 id 抛 `InvalidOperationException`。**
- `OpenAsync` 中"创建窗口对象"与"初始化并显示"是两个阶段。第二阶段失败时，
  窗口已从管理器移除并释放，异常继续上抛——**不会残留没有 WebView 的空壳窗口**。
- `CloseAsync` 对**不存在的 id 静默返回**（幂等，不抛异常）。
  它触发 `ClosingAsync` 裁决；被拒绝时窗口保持打开。
- 创建/释放顺序：先释放全部窗口，再释放环境。

### 3.2 `WebViewWindowOptions`

| 属性 | 默认 | 说明 |
|---|---|---|
| `Url` | `null` | 首次导航地址；null 时创建空白窗口 |
| `Title` | `null` | 为空时回退为 `Url.Host` |
| `Width` / `Height` | `1024` / `768` | 设备无关像素 |
| `MinWidth` / `MinHeight` | `0` | 最小尺寸 |
| `Topmost` | `false` | 置顶 |
| `Center` | `true` | 首次显示居中于目标显示器 |
| `Focus` | `true` | 首次显示后激活 |
| `ShowInTaskbar` | `true` | 任务栏显示；有 Owner 的窗口通常应设 false |
| `Resizable` | `true` | 关闭后同时禁用最大化按钮 |
| `TitleBarStyle` | `ChromeTitleBarStyle.Chrome` | 标题栏几何骨架（`WindowChromeKit` 枚举） |
| `TitleBarPalette` | `ChromeTitleBarPalette.Default` | 标题栏配色 |
| `AllowedOrigins` | `[]`（不限制） | Origin 白名单 |
| `ClosingAsync` | `null` | 关闭裁决；返回 `false` 拒绝关闭 |
| `OpenedAsync` | `null` | 首次导航完成并显示后触发 |
| `ClosedAsync` | `null` | 窗口关闭后触发一次 |
| `NewWindowRequestedAsync` | `null` | 页面请求新窗口；null 时在当前窗口内导航 |

### 3.3 线程模型

- 所有窗口创建、显示与 WebView2 交互**必须在传入的 `Dispatcher`（UI 线程）上**。
- `WebViewShell` 内部通过 `_dispatcher.InvokeAsync` 切回 UI 线程创建窗口，
  因此 `OpenAsync` 可以从任意线程调用（`CreateAsync` 同样要求提供 `Dispatcher`）。
- `WebViewShell` 的窗口字典当前**没有加锁**，`WindowCount` / `TryGetWindow` /
  `CloseAsync` 假定在 UI 线程调用。多线程访问是未定义行为（见第八节 D4）。

---

## 四、内部结构

目录**刻意保持扁平**：6 个源文件全部位于项目根目录，不设子目录。

```
src/BrowserShell.WebView.Wpf/
├── WebViewShell.cs                                127  入口：窗口托管
├── WebViewWindow.cs                               588  窗口本体（职责过载，见下）
├── WebViewWindowOptions.cs                         82  配置 + 关闭上下文
├── WebViewWindowEnvironment.cs                    122  环境 / Profile / 临时目录
├── WebView2InitializationCoordinator.cs            28  串行初始化
├── WebViewPresentationMask.cs                      97  加载遮罩（注入脚本）
├── BrowserShell.WebView.Wpf.csproj
└── README.md                                       52  使用说明
```

合计 **6 个 .cs / 1044 行**（共 8 个入库文件）。

**为什么扁平**：全部类型同属单一命名空间 `BrowserShell.WebView.Wpf`，仓库规模只有 6 个文件。
此时任何子目录都会造成"物理目录结构与逻辑命名空间不一致"，并让 1 个文件独占一层目录
（如原 `Windows/Coordination/`、`Windows/WebView/` 各只装 1 个文件）。
子目录带来的定位成本高于它提供的分类价值，故全部收敛到根目录。

> 历史说明：`Windows/` 及其下 `Coordination/`、`WebView/` 是 SDK 时代的遗留分层——
> 当年存在 `Instances/`、`Frame/`、`Interop/`、`Icons/`、`Pages/` 等多个目录，
> 各装有多个文件，分层是有意义的。瘦身后文件被大量删除，目录结构未同步收拢，
> 才出现"空壳两层、每层一个文件"的形态。本次重组已将其拍平。
>
> 若将来文件数显著增长（例如超过 15 个），再按**职责**而非历史沿革重新引入
> 子目录，并同步调整命名空间。

> 变更记录：原 9 文件 / 1211 行中的输入门控三件套
> （`NativeInput.cs` 21 行、`NativeWindowInputGate.cs` 96 行、`ModalWindowBlockState.cs` 35 行）
> 已按"无生产调用点"结论**删除**，详见 7.1。`WebViewWindow.cs` 同步移除
> `_inputGate` 字段、构造初始化与 `SourceInitialized` 订阅，及 3 个 public 成员。
>
> 数字漂移说明：`迁移报告.md` 第二节记的 `1149 行` 是瘦身提交 `c7278a8` 当时的值；
> 其后 `WebViewWindow` 的失败呈现与导航兜底改动使总数增至 1211 行，再经本次删除降至 1044 行。

### 4.1 `WebViewWindow` 的职责过载

588 行仍占全库近六成，单个文件内同时承担 6 件事：

| 关注点 | 大致位置 |
|---|---|
| 窗口外观应用（标题/尺寸/ResizeMode/标题栏） | `ApplyOptions` |
| 初始化时序（EnsureHandle → Show → 布局 → WebView） | `InitializeAndShowAsync` |
| WebView2 装配（设置、事件、脚本注入、首次导航） | `InitializeWebViewAsync` |
| 失败呈现（错误面板 UI） | `ShowInitializationFailure` |
| 关闭裁决与页面桥消息 | `RequestCloseAsync` / `HandleBridgeMessageAsync` |
| 注入脚本字面量 | `CreateWindowBridgeScript` |

这些关注点彼此独立（UI 文本、JS 字面量、时序控制混在一起），是可分离的。
拆分**不改变公开 API**，属于纯内部整理（见第八节 D1）。

---

## 五、关键时序

### 5.1 打开窗口

```
宿主: WebViewShell.CreateAsync(dispatcher)
        └─ WebViewWindowEnvironment.Create(...)   → 准备临时用户数据目录
           └─ InitializeAsync()                    → CoreWebView2Environment.CreateAsync

宿主: shell.OpenAsync(options, id)
        ├─ 校验 id 唯一性（重复即抛）
        ├─ dispatcher.InvokeAsync → new WebViewWindow(...)   ← 仅构造，不显示
        └─ window.InitializeAndShowAsync(token)
              ├─ EnsureHandle()            ← 必须先有 HWND，再创建 controller
              ├─ Show() + 居中/约束到工作区
              ├─ 等待 Loaded / Render 优先级排空
              └─ InitializeWebViewAsync(token)
                    ├─ 串行化 EnsureCoreWebView2Async
                    ├─ 收紧设置（禁 DevTools/右键/下载/权限/脚本对话框）
                    ├─ 注入遮罩脚本 + 页面桥脚本
                    ├─ 首次导航（30s 超时，失败不抛）
                    ├─ 揭开遮罩、显示 WebView
                    └─ Focus 时 Activate()
        └─ OpenedAsync 回调
```

**关键约束**：`EnsureHandle()` 必须在创建 WebView2 controller **之前**。
否则自绘标题栏会落在 DWM 尚未接管客户区的时间点，出现绘制错位。

**失败隔离**：窗口显示与 WebView2 初始化是两段独立的兜底。窗口先出来，
WebView 失败时保留窗口并显示原因，避免"一闪即消"。首次导航失败（网络/DNS/超时）
**不抛异常**，原因写入 `LastNavigationError`，调用方可重试。

### 5.2 关闭裁决

```
来源 A：标题栏关闭按钮 → WPF Closing 事件
来源 B：页面 browserShell.window.close() → WebMessage
来源 C：宿主 window.CloseAsync(source) / shell.CloseAsync(id, source)

        ┌─────────────────────────────────────────┐
        │ ClosingAsync == null ? 直接关闭          │
        │ 否则 → RequestCloseAsync(source, reqId) │
        │   ├─ 已有裁决在途 → 返回 CLOSE_PENDING   │
        │   ├─ accept  → CloseCore()               │
        │   └─ reject  → 保持打开                  │
        └─────────────────────────────────────────┘
                      ↓
        来源 B 额外回传 shellWindowCloseResult
        （accepted / code / message）
```

- 标题栏关闭通过 `eventArgs.Cancel = true` 拦截，转入裁决；`CloseCore()` 内部用
  `_forceClose` 放行真实的 `Close()`，避免二次裁决死循环。
- `CloseCore()` 会 `Hide()` 再 `Close()`，并把 `Owner` 置空。
- 裁决回调抛异常时按"拒绝关闭"处理，来源 B 还会回传 `HANDLER_FAILED`。

### 5.3 页面侧控制桥

注入的 `globalThis.browserShell.window`（`Object.freeze`）：

| 成员 | 行为 |
|---|---|
| `windowId` | 当前窗口标识 |
| `minimize()` / `maximize()` / `restore()` | 立即执行，无回执 |
| `close()` | 返回 `Promise<{accepted, code, message}>`，经 `requestId` 关联回执 |

页面 → 宿主消息类型为 `shellWindow`，宿主 → 页面回执类型为 `shellWindowCloseResult`。

### 5.4 加载遮罩

遮罩不依赖 WPF 层，而是注入到**页面文档内部**（`__softwarehub_presentation_mask__`，
`position: fixed` + 最高 z-index + closed shadow root），因此它天然覆盖 WebView2
自己的 HWND 区域，不受 WPF 合成影响。

- 初始化脚本立即插入遮罩，因此**首次导航一开始就被遮住**。
- 导航完成或初始化流程结束时，通过 `ExecuteScriptAsync` 调
  `__softwareHubSetPresentationMask(false, generation)` 揭开。
- 用单调递增 `generation` 防止乱序：`requestedGeneration < generation` 的请求被忽略，
  避免后发的"显示"被先发的"遮罩"覆盖。
- 遮罩操作失败被吞掉（`catch { }`）——**遮罩不可用时页面仍可见，不阻断导航**。

---

## 六、失败语义汇总

| 场景 | 行为 |
|---|---|
| WebView2 Runtime 缺失 | 窗口保留，内容区显示"无法初始化 WebView2"与原因；`LastNavigationError` 有值 |
| 首次导航失败/超时 | 不抛异常，`LastNavigationError` 记录原因，窗口可用 |
| Profile 创建失败 | 同 Runtime 缺失，走同一失败面板 |
| `EnsureCoreWebView2Async` 被关闭流程取消 | 视为正常取消，不报错 |
| `OpenAsync` 窗口构造后初始化失败 | 从管理器移除 + 释放 + 上抛，不留空壳 |
| 遮罩脚本执行失败 | 静默忽略 |
| 显示器热插拔导致约束失败 | 静默忽略，不影响窗口存续 |
| 关闭裁决回调抛异常 | 按拒绝处理，回传 `HANDLER_FAILED` |

安全默认值（`InitializeWebViewAsync` 中显式收紧）：

- `AreHostObjectsAllowed = false`
- `AreDefaultScriptDialogsEnabled = false`
- `AreDevToolsEnabled = false`
- `AreDefaultContextMenusEnabled = false`
- `IsStatusBarEnabled = false`
- 所有 `PermissionRequested` 一律 **Deny**
- 所有 `DownloadStarting` 一律 **Cancel**

即：**默认拒绝，由宿主按需放开**。当前版本没有提供放开这些开关的选项（见第八节 D5）。

---

## 七、待决事项

以下问题已核实，但**尚未定案**，需与项目维护者确认后再改代码。

### 7.1 模态输入门控 —— ✅ 已删除

**决定**：删除输入门控三件套。已从 `WebViewWindow.cs` 与测试中移除。

原状：`NativeWindowInputGate`（96 行）+ `ModalWindowBlockState`（35 行）+ `NativeInput`（21 行）
构成一套"阻止父窗口接收输入"的门控，但检索全仓库（含 sample、xaml、demo.html）：

| 成员 | 可见性 | 生产调用点 |
|---|---|---|
| `WebViewWindow.BlockForModalChild()` | **public** | **0** |
| `WebViewWindow.ReleaseModalChildBlock()` | **public** | **0** |
| `WebViewWindow.ModalReferenceCount` | **public** | **0** |
| `NativeWindowInputGate.SetResultPending` | internal | 仅测试 |
| `NativeWindowInputGate.IsInputBlocked` / `IsResultPending` | internal | 仅测试 |

**唯一消费者是它自己的测试**（`NativeWindowInputGateTests` 121 行、
`ModalWindowBlockStateTests` 45 行）。它来自 SDK 时代——原设计里 PageWindow 的模态
子窗口需要让父窗口"变灰"，而 PageWindow 已不在此库中。

**删除理由**（原选项 1）：

1. 零生产调用点，连示例与页面都不涉及。
2. 门控用 `EnableWindow(hwnd, false)` 禁用**顶层 HWND**。父窗口是 `ChromeWindow`
   （自绘标题栏），禁用后标题栏按钮同样失去响应，用户会看到一个"点不动"的窗口。
   测试断言的是 WPF 可视树仍 `IsEnabled`，但原生标题栏交互未被覆盖。
3. 当前无外部集成方，删除成本最低。

**删除内容**（路径为删除当时的位置；`Windows/Instances/` 目录随后在
目录重组中一并移除，见第四节）：

| 文件 | 处理 |
|---|---|
| `NativeInput.cs`（21 行） | 删除 |
| `Windows/Instances/NativeWindowInputGate.cs`（96 行） | 删除 |
| `Windows/Instances/ModalWindowBlockState.cs`（35 行） | 删除 |
| `tests/.../NativeWindowInputGateTests.cs`（121 行） | 删除 |
| `tests/.../ModalWindowBlockStateTests.cs`（45 行） | 删除 |

**`WebViewWindow.cs` 同步改动**：移除 `_inputGate` 字段、构造函数中的门控初始化、
`SourceInitialized` 订阅，以及 `BlockForModalChild()` / `ReleaseModalChildBlock()` /
`ModalReferenceCount` 三个 public 成员。

> 若将来重新引入模态子窗口需求，应从 git 历史取回这三个文件，并**重新设计**
> 阻塞语义（只挡内容区而非整个 HWND），而不是恢复 `EnableWindow` 的旧做法。

### 7.2 窗口自发关闭不通知 `WebViewShell`（缺陷）

`WebViewShell._windows` 只在两处移除：

- `WebViewShell.cs:89` —— `OpenAsync` 初始化失败时
- `WebViewShell.cs:107` —— `CloseAsync(id)` 被显式调用时

而用户**点标题栏 X** 关闭窗口时，路径是
`OnClosing → RequestCloseAsync → CloseCore() → Close()`，
**不经过 `WebViewShell.CloseAsync`**。`Closed` 事件只回调 `options.ClosedAsync`，
没有任何机制通知 `WebViewShell` 移除条目。

后果：

- `WindowCount` 虚高（包含已关闭窗口）
- `TryGetWindow` / `Windows` 会返回已关闭的死窗口
- 示例 `MainWindow.xaml.cs:122` 正是在 `ClosedAsync` 里读 `_shell.WindowCount` 显示窗口数，
  因此**示例界面上的窗口计数会失真**

这是行为缺陷而非风格问题，修复方向是让 `WebViewWindow` 在关闭时回调所有者
（例如注入一个内部 `onClosed` 或让 `WebViewShell` 订阅 `ClosedAsync`）。

### 7.3 文档与代码矛盾

`README.md:119` 写：

> `WebViewWindow` 本身即 `ChromeWindow` 派生类，宿主也可直接继承它扩展自定义行为。

但实际是 `public sealed class WebViewWindow`，且构造函数为 `internal`。
**宿主既不能继承，也不能直接 new。** 该句必须删除或改写。

### 7.4 页面脚本品牌残留

遮罩脚本内仍是旧的 `SoftwareHub` 品牌标识：

- `WebViewPresentationMask.cs:11` —— `const hostId = '__softwarehub_presentation_mask__'`
- `WebViewPresentationMask.cs:66` —— `globalThis.__softwareHubSetPresentationMask`
- `WebViewPresentationMask.cs:93` —— 同一全局名的调用

迁移报告记录"残留 `SoftwareHub` 标识符 = 0"，但**只统计了 C# 标识符，未覆盖内嵌 JS 字符串**。
这些名字会注入到每个页面，属于对外可见的实现痕迹。是否改名需考虑：一旦有页面
依赖该全局名（当前它是内部约定，未对外文档化），改名即为破坏性变更。

### 7.5 无调用点的成员

分两类，性质不同。

**（a）内部成员——属于实现残留**

| 成员 | 状态 |
|---|---|
| `WebViewPresentationMask.PostVisibility(core, visible, generation)` | **0 调用点**（实际走 `SetVisibilityAsync`） |

**（b）公开成员——没有任何仓内调用者**

这些是"对外承诺的能力"，示例未演示、测试未覆盖，因此**行为未经任何验证**：

| 成员 | 仓内调用点 |
|---|---|
| `WebViewWindow.ApplyOptions` | 0（仅构造函数内部调用一次） |
| `WebViewWindow.ClosePermanently` | 0 |
| `WebViewWindow.IsClosed` | 0 |
| `WebViewWindow.NavigateAsync` / `ReloadAsync` | 0 |
| `WebViewWindow.ShowWindowAsync` / `HideAsync` | 0 |
| `WebViewWindow.MinimizeAsync` / `MaximizeAsync` / `RestoreAsync` | 0 |
| `WebViewWindow.SetTitleAsync` | 0 |

需要区分对待：

- `ApplyOptions` / `ClosePermanently` / `IsClosed` 属于**冗余公开面**——
  前者只在构造时被调用一次（公开出去意义不明），`ClosePermanently` 绕过裁决
  与"关闭必须经裁决"的设计意图相冲突。建议收回或删除。
- 窗口命令类（`Minimize` / `Maximize` / `Restore` / `Hide` / `Show` / `SetTitle` /
  `Navigate` / `Reload`）是**合理的对外能力**，但示例与测试都没碰。
  建议在示例中演示、或补测试，否则无法确认它们真的可用。

> 注：页面桥 `browserShell.window.*` 走的是 `WebViewWindow` 内部字段
> （`WindowState = ...`），**不经过**上述公开方法，因此这些方法实际上是两条并行的
> 实现路径。页面桥能工作，不代表这些公开方法也正确。
>
> 两条路径已经出现**行为不一致**：`RestoreAsync()` 在还原后调用了
> `WindowPlacementService.ConstrainToWorkArea(this)`，而页面桥的 `restore` 分支
> （`WebViewWindow.cs:448`）只设 `WindowState = WindowState.Normal`，不做工作区约束。
> 窗口从最小化还原到已拔掉的显示器上时，两者结果会不同。

### 7.6 页面桥消息解析无防护（缺陷）

`WebViewWindow.cs:181` 以 fire-and-forget 方式接收页面消息：

```csharp
core.WebMessageReceived += (_, eventArgs) => _ = HandleBridgeMessageAsync(eventArgs.WebMessageAsJson);
```

而 `HandleBridgeMessageAsync`（`WebViewWindow.cs:436`）**没有任何 try/catch**，
第一行就直接 `JsonDocument.Parse(json)`，紧接着 `root.TryGetProperty(...)`。

问题在于 `JsonElement.TryGetProperty` 的文档行为：当 `ValueKind` 不是 `Object` 时
**抛 `InvalidOperationException`**。而页面通过 `chrome.webview.postMessage(value)`
可以发送**任意 JSON 值**，不限于对象：

```js
chrome.webview.postMessage(null);   // → "null"    → ValueKind = Null   → 抛异常
chrome.webview.postMessage(42);     // → "42"      → ValueKind = Number → 抛异常
chrome.webview.postMessage("hi");   // → "\"hi\""  → ValueKind = String → 抛异常
```

由于该任务无人等待，异常会变成 **unobserved task exception**：轻则被静默吞掉，
重则在特定配置下触发进程级异常处理。

需要说明的是，`AreHostObjectsAllowed = false` **不会**阻止
`chrome.webview.postMessage`——这是 WebView2 的宿主通信通道，任何被承载的页面
（包括第三方页面）都能触发上述路径。

对照之下，`RequestCloseAsync` 是**有** try/catch 的（`WebViewWindow.cs:423`），
所以这是一个局部的防护遗漏，不是整体风格。

修复方向：解析前判断 `ValueKind == JsonValueKind.Object`，并给整个方法加 try/catch。

---

## 八、演进方向（候选，未定案）

### D1. 拆分 `WebViewWindow`（纯内部整理，不改 API）

建议按关注点拆成若干内部文件，`WebViewWindow` 保留为组合根：

| 候选拆分 | 内容 |
|---|---|
| 窗口外观 | `ApplyOptions` 相关 |
| 初始化时序 | `InitializeAndShowAsync` / `InitializeWebViewAsync` |
| 失败呈现 | `ShowInitializationFailure` + 初始化面板 UI |
| 页面桥 | `CreateWindowBridgeScript` / `HandleBridgeMessageAsync` / `PostCloseResult` |
| 关闭裁决 | `RequestCloseAsync` / `OnClosing` / `CloseCore` |

### D2. 封装力度：是否继续暴露底层类型

当前公开面泄漏了外部依赖的具体类型：

| 泄漏点 | 影响 |
|---|---|
| `WebViewWindow : ChromeWindow` | 换掉 `WindowChromeKit` 即破坏性变更；`ChromeWindow` 全部成员成为隐含公开面 |
| `WebViewWindowEnvironment.Core → CoreWebView2Environment` | 原生 WebView2 对象直接交出去，封装失效 |
| `WebViewWindowEnvironment.CreateControllerOptions(string)` | 同上 |
| `WebViewWindowOptions.TitleBarStyle/Palette` | 绑定 `WindowChromeKit` 枚举 |

取舍：

- **收紧**：把原生对象改为 `internal`，只暴露必要的能力接口；降低外部耦合。
- **开放**：承认本库面向"高级宿主"，故意交出 Win32/WebView2 控制权。

需要明确取舍，因为它决定 D3 的可行性。注意：**当前无外部集成方**
（本仓库内仅示例与测试引用），是调整公开面的成本最低点。

### D3. 同源约束是否应留在本库

`AllowedOrigins` 同时作用于 `NavigationStarting` 拦截与 `NewWindowRequested`。
它是**安全策略**，但只支持"Origin 白名单"这一种形态。复杂策略（按路径、按方法、
按导航来源）无法表达。可考虑：保留简单白名单，同时提供"策略回调"逃生口。

### D4. 线程安全

`WebViewShell` 的字典与 `_disposed` 标志无锁。若承诺"可从任意线程调用"，
需要加锁或明确只允许 UI 线程调用并在文档中声明。当前是**隐含的 UI 线程约定**。

### D5. 安全开关的开放程度

当前硬编码"全部拒绝"（权限、下载、DevTools、右键、脚本对话框），
且无选项可放开。对纯展示型宿主合适；对需要 DevTools 调试或文件下载的业务
则不可用。可考虑增加 `WebViewWindowOptions` 开关，默认保持拒绝。

---

## 九、测试现状

7.1 删除完成后，测试只剩：

| 测试文件 | 行数 | 覆盖对象 |
|---|---|---|
| `WebViewPresentationMaskTests.cs` | 24 | 遮罩命令序列化与脚本特征 |

合计 **1 文件 / 24 行**。

（删除前为 3 文件 / 190 行，其中 166 行测的是零生产调用点的输入门控。）

覆盖缺口（与第七节问题对应）：

- `WebViewShell` 的窗口托管逻辑（含 7.2 的缺陷）**无测试**
- `WebViewWindow` 的关闭裁决路径**无测试**
- `WebViewWindowEnvironment` 的 Profile 隔离与临时目录回收**无测试**
- 页面桥的消息协议（含 7.6 的解析缺陷）**无测试**

即：原本的测试投入集中在**已被证明无生产价值**的部分（166/190 行），
删除后有效覆盖率接近于零。**补测是当前优先级最高的工程任务**，
首推 `WebViewShell` 的窗口托管与 `WebViewWindow` 的关闭裁决。

`dotnet test` 需在 Windows 上运行（依赖 `Microsoft.WindowsDesktop.App`），
Linux 上无法执行。

---

## 十、附：公开 API 一览

```csharp
namespace BrowserShell.WebView.Wpf;

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

    public Task CloseAsync(string id, string source = "Service");
    public ValueTask DisposeAsync();
}

public sealed class WebViewWindow : ChromeWindow, IAsyncDisposable
{
    public string Id { get; }
    public bool IsClosed { get; }
    public string? LastNavigationError { get; }

    public Task InitializeAndShowAsync(CancellationToken token = default);
    public void ApplyOptions(WebViewWindowOptions options);
    public Task NavigateAsync(Uri target);
    public Task ReloadAsync();
    public Task SetTitleAsync(string title);
    public Task ShowWindowAsync();
    public Task HideAsync();
    public Task MinimizeAsync();
    public Task MaximizeAsync();
    public Task RestoreAsync();
    public Task CloseAsync(string source = "Service");
    public void ClosePermanently();
    public ValueTask DisposeAsync();
}

public sealed class WebViewWindowOptions { /* 见 3.2 */ }

public sealed class WebViewWindowClosingContext
{
    public WebViewWindow Window { get; }
    public string Source { get; }   // "WindowChrome" | "Page" | 宿主自定义
}

public sealed class WebViewWindowEnvironment : IAsyncDisposable
{
    public static WebViewWindowEnvironment Create(
        Dispatcher dispatcher,
        string? browserExecutableFolder = null,
        string? userDataFolder = null);

    public CoreWebView2Environment Core { get; }                       // 泄漏原生类型
    public CoreWebView2ControllerOptions CreateControllerOptions(string profileId);  // 同上
    public Task InitializeAsync(CancellationToken token = default);
    public ValueTask DisposeAsync();
}
```

---

## 相关文档

- [`../README.md`](../README.md) —— 仓库总览
- [`迁移报告.md`](迁移报告.md) —— 从 `SoftwareHub.DesktopAgent` 迁入与后续瘦身的完整记录
- [`../src/BrowserShell.WebView.Wpf/README.md`](../src/BrowserShell.WebView.Wpf/README.md) —— 面向使用者的快速开始
