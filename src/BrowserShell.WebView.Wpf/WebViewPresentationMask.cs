using System.Text.Json;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace BrowserShell.WebView.Wpf;

/// <summary>在标准 WebView2 自身的 HWND 内遮盖导航和复用切换过程。</summary>
internal static class WebViewPresentationMask
{
    /// <summary>
    /// 生成注入页面的遮罩脚本。
    /// </summary>
    /// <param name="backgroundColor">
    /// 遮罩底色，应与 <see cref="WebViewWindowOptions.BackgroundColor"/> 一致，
    /// 否则深色页面在加载期间会露出浅色遮罩（即"白闪"）。
    /// </param>
    /// <remarks>
    /// <para>脚本在 <c>AddScriptToExecuteOnDocumentCreatedAsync</c> 中注入，于页面脚本之前执行，
    /// 因此遮罩在文档创建后立即建立，覆盖此后直到 <c>NavigationCompleted</c> 的整段加载期。</para>
    /// <para>遮罩挂在<b>闭合的 shadow root</b> 内：宿主页面的 CSS 无法选中或覆盖它，
    /// 页面自身的样式重置也不会影响遮罩外观。</para>
    /// <para>前景色由底色亮度推导（见 <see cref="PickForeground"/>），
    /// 以免深色底色上的指示文字与转圈不可见。</para>
    /// </remarks>
    public static string CreateInitializationScript(Color backgroundColor)
    {
        var background = ToCss(backgroundColor);
        var spinnerTrack = ToCss(PickTrack(backgroundColor));
        var spinnerHead = ToCss(PickAccent(backgroundColor));
        var foreground = ToCss(PickForeground(backgroundColor));
        return $$"""
        (() => {
          const hostId = '__browserShellPresentationMask__';
          let visible = true;
          let generation = 0;

          // 页面 body.onload 迟迟不触发时（例如某个 iframe 或子资源悬挂、连接未正常终止），
          // WebView2 的 NavigationCompleted 不会到来，宿主也就不会发来揭罩指令——
          // 遮罩会永久盖住页面。此定时器是兜底：遮罩显示期间最多等 30 秒（与宿主
          // 首次导航超时一致）就自动揭开，宁可闪现半成品页面也不永久卡死。
          const AUTO_REVEAL_MS = 30000;
          let autoRevealToken = 0;
          const cancelAutoReveal = () => {
            if (autoRevealToken) { clearTimeout(autoRevealToken); autoRevealToken = 0; }
          };
          const scheduleAutoReveal = () => {
            cancelAutoReveal();
            autoRevealToken = setTimeout(() => {
              autoRevealToken = 0;
              visible = false;
              ensureMask();
            }, AUTO_REVEAL_MS);
          };

          const ensureMask = () => {
            const root = document.documentElement;
            if (!root) return null;
            let host = document.getElementById(hostId);
            if (!host) {
              host = document.createElement('div');
              host.id = hostId;
              host.setAttribute('aria-hidden', 'true');
              Object.assign(host.style, {
                position: 'fixed', inset: '0', zIndex: '2147483647',
                display: 'grid', placeItems: 'center', background: '{{background}}',
                pointerEvents: 'auto'
              });
              const shadow = host.attachShadow({ mode: 'closed' });
              const content = document.createElement('div');
              Object.assign(content.style, {
                color:'{{foreground}}', font:'14px "Segoe UI",sans-serif', textAlign:'center'
              });
              const spinner = document.createElement('i');
              Object.assign(spinner.style, {
                display:'block', width:'26px', height:'26px', margin:'0 auto 12px',
                border:'3px solid {{spinnerTrack}}', borderTopColor:'{{spinnerHead}}', borderRadius:'50%'
              });
              spinner.animate(
                [{ transform:'rotate(0deg)' }, { transform:'rotate(360deg)' }],
                { duration:800, iterations:Infinity });
              const label = document.createElement('span');
              label.textContent = '正在加载…';
              content.append(spinner, label);
              shadow.appendChild(content);
              root.appendChild(host);
            }
            host.style.display = visible ? 'grid' : 'none';
            return host;
          };

          ensureMask();
          if (!document.documentElement) {
            new MutationObserver((_, observer) => {
              if (ensureMask()) observer.disconnect();
            }).observe(document, { childList: true, subtree: true });
          }
          // 首次建立遮罩即开始计时：即便宿主侧因 WebView2 初始化失败等原因
          // 从未发来揭罩指令，遮罩也会在超时后自动揭开。
          scheduleAutoReveal();

          const setVisibility = (nextVisible, nextGeneration) => {
            const requestedGeneration = Number(nextGeneration) || 0;
            if (requestedGeneration < generation) return ensureMask() !== null;
            generation = requestedGeneration;
            visible = nextVisible === true;
            // 揭罩即取消兜底定时器；重新盖罩（新一轮导航）则重新计时。
            if (visible) scheduleAutoReveal(); else cancelAutoReveal();
            const mask = ensureMask();
            return mask !== null;
          };
          Object.defineProperty(globalThis, '__browserShellSetPresentationMask', {
            value: setVisibility
          });
          chrome.webview.addEventListener('message', event => {
            const message = event.data;
            if (message?.type !== 'presentationMask') return;
            setVisibility(message.visible, message.generation);
          });
        })();
        """;
    }

    /// <summary>初始化指示文案与转圈所依赖的底色（未加载出内容时显示的纯色）。</summary>
    public static Color DefaultBackgroundColor => Colors.White;

    /// <summary>把颜色转为 CSS 的 <c>#rrggbb</c> 形式（CSS 不识别 WPF 的 <c>#aarrggbb</c>）。</summary>
    internal static string ToCss(Color color) =>
        $"#{color.R:x2}{color.G:x2}{color.B:x2}";

    /// <summary>按 sRGB 相对亮度判断底色明暗，据此选择可读的前景色。</summary>
    /// <remarks>系数取 Rec. 709 亮度权重，与 <c>prefers-color-scheme</c> 的常见判定一致。</remarks>
    internal static Color PickForeground(Color background) =>
        IsDark(background) ? Color.FromRgb(0xB0, 0xB0, 0xB0) : Color.FromRgb(0x66, 0x66, 0x66);

    /// <summary>转圈的底环颜色：浅底用浅灰，深底用深灰。</summary>
    internal static Color PickTrack(Color background) =>
        IsDark(background) ? Color.FromRgb(0x4A, 0x4A, 0x4A) : Color.FromRgb(0xDD, 0xDD, 0xDD);

    /// <summary>转圈的旋转头颜色：浅底用蓝色，深底用亮蓝，保证两种底色下都可见。</summary>
    internal static Color PickAccent(Color background) =>
        IsDark(background) ? Color.FromRgb(0x5A, 0xA9, 0xF0) : Color.FromRgb(0x28, 0x78, 0xD7);

    private static bool IsDark(Color color) =>
        (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) < 128;

    public static string CreateCommand(bool visible, long generation) =>
        JsonSerializer.Serialize(new { type = "presentationMask", visible, generation });

    public static void PostVisibility(CoreWebView2 core, bool visible, long generation)
    {
        ArgumentNullException.ThrowIfNull(core);
        core.PostWebMessageAsJson(CreateCommand(visible, generation));
    }

    public static async Task<bool> SetVisibilityAsync(
        CoreWebView2 core,
        bool visible,
        long generation,
        CancellationToken token)
    {
        var script =
            $"globalThis.__browserShellSetPresentationMask?.({visible.ToString().ToLowerInvariant()}, {generation}) === true";
        var result = await core.ExecuteScriptAsync(script).WaitAsync(TimeSpan.FromSeconds(15), token);
        return string.Equals(result, "true", StringComparison.Ordinal);
    }
}
