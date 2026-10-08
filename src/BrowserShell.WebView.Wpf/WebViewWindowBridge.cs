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
/// <para><b>已知不足</b>：<c>close</c> 的 Promise 没有超时。若宿主未回执
/// （例如回执时 WebView 已不可用），该 Promise 永久挂起——页面侧需自行加超时兜底。</para>
///
/// <para><b>范围</b>：WebView2 只在<b>顶层文档</b>触发 <c>WebMessageReceived</c>，
/// iframe 内的页面拿不到此桥。</para>
/// </remarks>
internal static class WebViewWindowBridge
{
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
        return $$"""
        (() => {
          const send = (operation, value, requestId) => chrome.webview.postMessage({
            type: 'shellWindow', operation, value: value ?? null, requestId: requestId ?? null
          });
          const closeRequests = new Map();
          chrome.webview.addEventListener('message', event => {
            const message = event.data;
            if (message?.type !== 'shellWindowCloseResult') return;
            const resolve = closeRequests.get(message.requestId);
            if (!resolve) return;
            closeRequests.delete(message.requestId);
            resolve(Object.freeze({
              accepted: message.accepted === true,
              code: message.code ?? null,
              message: message.message ?? null
            }));
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
                closeRequests.set(requestId, resolve);
                send('close', 'Page', requestId);
              })
            }) })
          });
        })();
        """;
    }
}
