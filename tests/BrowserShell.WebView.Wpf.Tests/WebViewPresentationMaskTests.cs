using System.Text.Json;

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
        Assert.DoesNotContain("presentationMaskShown", WebViewPresentationMask.InitializationScript, StringComparison.Ordinal);
        Assert.DoesNotContain("requestAnimationFrame", WebViewPresentationMask.InitializationScript, StringComparison.Ordinal);
        Assert.Contains("requestedGeneration < generation", WebViewPresentationMask.InitializationScript, StringComparison.Ordinal);
    }
}
