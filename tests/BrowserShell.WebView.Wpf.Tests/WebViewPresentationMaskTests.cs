using System.Text.Json;
using System.Windows.Media;

namespace BrowserShell.WebView.Wpf.Tests;

public sealed class WebViewPresentationMaskTests
{
    [Fact]
    public void CreateCommand_PreservesVisibilityAndGeneration()
    {
        using var document = JsonDocument.Parse(WebViewPresentationMask.CreateCommand(true, 42));

        Assert.Equal("presentationMask", document.RootElement.GetProperty("type").GetString());
        Assert.True(document.RootElement.GetProperty("visible").GetBoolean());
        Assert.Equal(42, document.RootElement.GetProperty("generation").GetInt64());
    }

    [Fact]
    public void InitializationScript_DoesNotWaitForAnOccludedAnimationFrame()
    {
        var script = WebViewPresentationMask.CreateInitializationScript(Colors.White);

        Assert.DoesNotContain("presentationMaskShown", script, StringComparison.Ordinal);
        Assert.DoesNotContain("requestAnimationFrame", script, StringComparison.Ordinal);
        Assert.Contains("requestedGeneration < generation", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// 揭罩的正常路径必须挂在 <c>DOMContentLoaded</c>（文档解析完成）上，而不是等
    /// 宿主随 <c>body.onload</c> 触发的揭罩指令：页面上任何一个悬挂的 iframe/子资源
    /// 都会让 onload 迟迟不来，遮罩就会多盖几十秒甚至永久盖住（2.1.1 实测 30 秒）。
    /// </summary>
    [Fact]
    public void InitializationScript_RevealsOnDomContentLoaded()
    {
        var script = WebViewPresentationMask.CreateInitializationScript(Colors.White);

        Assert.Contains("addEventListener('DOMContentLoaded'", script, StringComparison.Ordinal);
        Assert.Contains("revealWhenParsed", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// 遮罩必须有自动揭开兜底：页面 <c>body.onload</c> 因 iframe/子资源悬挂而不触发时，
    /// WebView2 的 <c>NavigationCompleted</c> 不会到来，宿主不会发来揭罩指令——
    /// 没有兜底遮罩就永久卡死（2.1.0 实测缺陷）。DOMContentLoaded 揭罩是正常路径，
    /// 此定时器仅覆盖"注入脚本异常、宿主从未参与"等边角。
    /// </summary>
    [Fact]
    public void InitializationScript_AutoRevealsMaskAfterTimeout()
    {
        var script = WebViewPresentationMask.CreateInitializationScript(Colors.White);

        Assert.Contains("scheduleAutoReveal", script, StringComparison.Ordinal);
        Assert.Contains("clearTimeout", script, StringComparison.Ordinal);
        Assert.Contains("AUTO_REVEAL_MS = 30000", script, StringComparison.Ordinal);
    }

    /// <summary>兜底定时器必须在遮罩建立时立即启动，而不是等宿主发指令——
    /// 否则 WebView2 初始化失败等"宿主从未发指令"的场景依然永久卡死。</summary>
    [Fact]
    public void InitializationScript_StartsAutoRevealImmediately()
    {
        var script = WebViewPresentationMask.CreateInitializationScript(Colors.White);

        // ensureMask() 首次调用之后必须紧跟着 scheduleAutoReveal() 启动兜底。
        var firstEnsure = script.IndexOf("ensureMask();", StringComparison.Ordinal);
        var scheduler = script.IndexOf("scheduleAutoReveal();", StringComparison.Ordinal);
        Assert.True(firstEnsure >= 0 && scheduler > firstEnsure, "遮罩建立后必须立即启动自动揭开。");
    }

    /// <summary>
    /// 遮罩底色必须取自 <c>BackgroundColor</c>：写死白色正是深色页面"白闪"的成因（设计文档 6.1）。
    /// </summary>
    [Fact]
    public void InitializationScript_UsesConfiguredBackgroundColor()
    {
        var script = WebViewPresentationMask.CreateInitializationScript(Color.FromRgb(0x1B, 0x1B, 0x1F));

        Assert.Contains("#1b1b1f", script, StringComparison.Ordinal);
        Assert.DoesNotContain("#ffffff", script, StringComparison.Ordinal);
    }

    /// <summary>CSS 只认 <c>#rrggbb</c>；WPF 的 <c>#aarrggbb</c> 会被静默忽略。</summary>
    [Theory]
    [InlineData(0x1B, 0x1B, 0x1F, "#1b1b1f")]
    [InlineData(0xFF, 0xFF, 0xFF, "#ffffff")]
    [InlineData(0x00, 0x00, 0x00, "#000000")]
    public void ToCss_EmitsSixDigitHex(byte r, byte g, byte b, string expected) =>
        Assert.Equal(expected, WebViewPresentationMask.ToCss(Color.FromRgb(r, g, b)));

    /// <summary>深色底必须配亮前景，否则"正在加载…"在深色上不可见。</summary>
    [Fact]
    public void PickForeground_ContrastsWithBackground()
    {
        var onDark = WebViewPresentationMask.PickForeground(Color.FromRgb(0x1B, 0x1B, 0x1F));
        var onLight = WebViewPresentationMask.PickForeground(Colors.White);

        Assert.True(Luminance(onDark) > Luminance(Color.FromRgb(0x1B, 0x1B, 0x1F)));
        Assert.True(Luminance(onLight) < Luminance(Colors.White));
        Assert.NotEqual(onDark, onLight);
    }

    /// <summary>转圈颜色在明暗两侧都必须可见，因此取值应不同。</summary>
    [Fact]
    public void PickAccent_DiffersAcrossLightAndDark()
    {
        var onDark = WebViewPresentationMask.PickAccent(Color.FromRgb(0x10, 0x10, 0x10));
        var onLight = WebViewPresentationMask.PickAccent(Colors.White);

        Assert.NotEqual(onDark, onLight);
        Assert.True(Luminance(onDark) > Luminance(onLight));
    }

    private static double Luminance(Color color) =>
        0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B;
}
