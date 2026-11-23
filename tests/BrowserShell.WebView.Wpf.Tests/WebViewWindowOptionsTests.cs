namespace BrowserShell.WebView.Wpf.Tests;

/// <summary>
/// <see cref="WebViewWindowOptions.WaitForFirstScreen"/> 的默认值与语义。
/// </summary>
/// <remarks>
/// <para>这个默认值直接决定 <c>OpenAsync</c> 是否会被页面里的慢资源/悬挂资源拖住。
/// 曾经固定等 <c>NavigationCompleted</c>（= <c>body.onload</c>），
/// 实测把 <c>OpenAsync</c> 从约 0.9 秒拖到 6~30 秒，
/// 并被消费方误判为"WebView2 环境初始化慢"（环境初始化实测约 15ms）。</para>
/// </remarks>
public sealed class WebViewWindowOptionsTests
{
    /// <summary>默认不等待：这是性能修复的核心，若被改回 true，慢页面会重新拖住 OpenAsync。</summary>
    [Fact]
    public void WaitForFirstScreen_DefaultsToFalse() =>
        Assert.False(new WebViewWindowOptions().WaitForFirstScreen);

    /// <summary>显式设 true 时保留"等导航结束"的旧语义，供确有需要的调用方使用。</summary>
    [Fact]
    public void WaitForFirstScreen_CanBeEnabled() =>
        Assert.True(new WebViewWindowOptions { WaitForFirstScreen = true }.WaitForFirstScreen);

    /// <summary>
    /// 默认应把键盘焦点交给页面。
    /// </summary>
    /// <remarks>
    /// <para>只调 <c>Window.Activate()</c> 时焦点停在窗口本身，页面拿不到输入：
    /// 实测页面 <c>document.hasFocus() == false</c>、<c>activeElement == BODY</c>，
    /// 用户必须先在页面里点一下才能打字。改为默认移交焦点后，
    /// 测得 <c>hasFocus == true</c>、<c>activeElement == INPUT</c>。</para>
    /// </remarks>
    [Fact]
    public void FocusPage_DefaultsToTrue() =>
        Assert.True(new WebViewWindowOptions().FocusPage);

    /// <summary>纯展示页可以关掉页面聚焦，此时只激活窗口。</summary>
    [Fact]
    public void FocusPage_CanBeDisabled() =>
        Assert.False(new WebViewWindowOptions { FocusPage = false }.FocusPage);

    /// <summary>窗口激活与页面聚焦是两个独立开关。</summary>
    [Fact]
    public void FocusAndFocusPage_AreIndependent()
    {
        var windowOnly = new WebViewWindowOptions { Focus = true, FocusPage = false };
        Assert.True(windowOnly.Focus);
        Assert.False(windowOnly.FocusPage);

        var neither = new WebViewWindowOptions { Focus = false, FocusPage = false };
        Assert.False(neither.Focus);
        Assert.False(neither.FocusPage);
    }

    /// <summary>
    /// 其余默认值不受新增属性影响（向后兼容）：尺寸、居中等保持原样。
    /// </summary>
    [Fact]
    public void OtherDefaults_Unchanged()
    {
        var options = new WebViewWindowOptions();

        Assert.Equal(1024, options.Width);
        Assert.Equal(768, options.Height);
        Assert.True(options.Center);
        Assert.True(options.Focus);
        Assert.True(options.ShowInTaskbar);
        Assert.True(options.Resizable);
        Assert.False(options.Topmost);
        Assert.Null(options.Left);
        Assert.Null(options.Top);
        Assert.Null(options.Url);
        Assert.Empty(options.AllowedOrigins);
    }
}
