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
