using System.Text.Json;

namespace BrowserShell.WebView.Wpf;

/// <summary>
/// 页面侧窗口控制桥：注入脚本。脚本暴露 <c>globalThis.browserShell.window</c>。
/// </summary>
/// <remarks>
/// <para><b>通道</b>：WebView2 的 web message（页面 <c>chrome.webview.postMessage</c>
/// ↔ 宿主 <c>CoreWebView2.WebMessageReceived</c>），因此要求
/// <c>CoreWebView2Settings.IsWebMessageEnabled</c> 保持默认的 <c>true</c>。</para>
///
/// <para><b>协议</b>（与宿主侧的处理代码必须一致）：</para>
/// <list type="bullet">
///   <item><description>页面 → 宿主：<c>{ type: 'shellWindow', operation, value, requestId }</c>，
///   其中 <c>operation</c> 为 <c>minimize</c>／<c>maximize</c>／<c>restore</c>／<c>close</c>。</description></item>
///   <item><description>宿主 → 页面：<c>{ type: 'shellWindowCloseResult', requestId, accepted, code, message }</c>，
///   仅回应带 <c>requestId</c> 的 <c>close</c>。</description></item>
/// </list>
/// <para>三个窗口状态操作是单向调用；<c>close</c> 返回 Promise，经 <c>requestId</c> 与回执配对。
/// 页面发起的关闭在宿主侧以来源 <c>"Page"</c> 进入关闭裁决。</para>
///
/// <para><b>close 超时</b>：回执可能永不到达——宿主在裁决期间崩溃，或
/// <c>PostCloseResult</c> 因 <c>CoreWebView2</c> 已不可用而静默跳过（它用 <c>?.</c>）。
/// 因此 <c>close</c> 自带 <see cref="CloseTimeout"/> 超时，超时兑现
/// <c>{ accepted:false, code:'TIMEOUT' }</c> 并清理挂起条目，避免 Promise 永久挂起、
/// 页面 await 处再也走不下去。</para>
///
/// <para><b>范围</b>：WebView2 只在<b>顶层文档</b>触发 <c>WebMessageReceived</c>，
/// iframe 内的页面拿不到此桥。</para>
/// </remarks>
internal static class WebViewWindowBridge
{
    /// <summary>
    /// 页面侧 <c>close()</c> 等待宿主回执的超时。
    /// </summary>
    /// <remarks>取值需大于宿主裁决的合理耗时（<c>ClosingAsync</c> 可能弹确认框或走网络），
    /// 又不能让页面等得过久。</remarks>
    public static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 生成注入页面的桥脚本。
    /// </summary>
    /// <param name="windowId">当前窗口标识，作为 <c>browserShell.window.windowId</c> 暴露给页面。</param>
    /// <remarks>
    /// 脚本经 <c>AddScriptToExecuteOnDocumentCreatedAsync</c> 注入，在页面自身脚本之前执行，
    /// 因此页面可同步读取该对象，无需等待 DOM 就绪。
    /// </remarks>
    public static string CreateScript(string windowId)
    {
        var windowIdJson = JsonSerializer.Serialize(windowId);
        var timeoutMs = (int)CloseTimeout.TotalMilliseconds;
        return $$"""
        (() => {
          const send = (operation, value, requestId) => chrome.webview.postMessage({
            type: 'shellWindow', operation, value: value ?? null, requestId: requestId ?? null
          });
          const closeRequests = new Map();
          const settle = (requestId, result) => {
            const pending = closeRequests.get(requestId);
            if (!pending) return;
            closeRequests.delete(requestId);
            clearTimeout(pending.timer);
            pending.resolve(Object.freeze(result));
          };
          chrome.webview.addEventListener('message', event => {
            const message = event.data;
            if (message?.type !== 'shellWindowCloseResult') return;
            settle(message.requestId, {
              accepted: message.accepted === true,
              code: message.code ?? null,
              message: message.message ?? null
            });
          });
          const current = globalThis.browserShell ?? {};
          Object.defineProperty(globalThis, 'browserShell', {
            configurable: true,
            value: Object.freeze({ ...current, window: Object.freeze({
              windowId: {{windowIdJson}},
              minimize: () => send('minimize'),
              maximize: () => send('maximize'),
              restore: () => send('restore'),
              close: () => new Promise(resolve => {
                const requestId = crypto.randomUUID();
                const timer = setTimeout(() => settle(requestId, {
                  accepted: false,
                  code: 'TIMEOUT',
                  message: '宿主未在超时内响应关闭请求。'
                }), {{timeoutMs}});
                closeRequests.set(requestId, { resolve, timer });
                send('close', 'Page', requestId);
              })
            }) })
          });
        })();
        """;
    }
}
