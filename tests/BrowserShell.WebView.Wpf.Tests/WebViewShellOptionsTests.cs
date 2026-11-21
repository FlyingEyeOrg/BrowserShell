using System.IO;
using BrowserShell.WebView.Wpf;
using Xunit;

namespace BrowserShell.WebView.Wpf.Tests;

/// <summary>
/// <see cref="WebViewShellOptions"/> 的缓存目录解析规则。
/// </summary>
/// <remarks>
/// <para>这一组断言锁定一个安全与稳定性修复：默认目录曾放在 <c>%TEMP%</c> 下，
/// 而该位置的 ACL 对本机其他用户与沙箱进程是 <c>Modify</c>（实测），
/// 且磁盘清理／Storage Sense 会删除临时文件——一删即登录态全丢。</para>
/// <para>现在默认改到 <c>%LOCALAPPDATA%</c>（仅当前用户可访问、不被清理工具删除）。</para>
/// </remarks>
public sealed class WebViewShellOptionsTests
{
    /// <summary>默认必须落在 LocalApplicationData，而不是 Temp。</summary>
    [Fact]
    public void ResolveUserDataFolder_DefaultsToLocalApplicationData()
    {
        var options = new WebViewShellOptions();
        var folder = options.ResolveUserDataFolder();

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.StartsWith(localAppData, folder, StringComparison.OrdinalIgnoreCase);

        // 明确排除 Temp：这正是被修掉的旧行为。
        var temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        Assert.DoesNotContain(temp, folder, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>目录形如 {LOCALAPPDATA}\BrowserShell\{应用名}。</summary>
    [Fact]
    public void ResolveUserDataFolder_UsesProductAndApplicationFolderName()
    {
        var options = new WebViewShellOptions { ApplicationFolderName = "SoftwareHub" };
        var folder = options.ResolveUserDataFolder();

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var expected = Path.Combine(localAppData, WebViewShellOptions.DefaultProductFolderName, "SoftwareHub");
        Assert.Equal(Path.GetFullPath(expected), folder, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>显式 UserDataFolder 完全接管，忽略其余拼接项。</summary>
    [Fact]
    public void ResolveUserDataFolder_ExplicitFolderWins()
    {
        var custom = Path.Combine(Path.GetTempPath(), "explicit-cache");
        var options = new WebViewShellOptions
        {
            UserDataFolder = custom,
            ApplicationFolderName = "Ignored",
            ProductFolderName = "IgnoredToo",
            UserDataFolderRoot = UserDataFolderLocation.RoamingApplicationData,
        };

        Assert.Equal(Path.GetFullPath(custom), options.ResolveUserDataFolder(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Location=Temp 时允许回到临时目录（供测试等场景显式选择）。</summary>
    [Fact]
    public void ResolveUserDataFolder_SupportsTempWhenExplicitlyRequested()
    {
        var options = new WebViewShellOptions
        {
            UserDataFolderRoot = UserDataFolderLocation.Temp,
            ApplicationFolderName = "SomeApp",
        };
        var folder = options.ResolveUserDataFolder();

        var temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        Assert.StartsWith(temp, folder, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Location=Custom 但未给 UserDataFolder 属于调用方错误，必须显式抛出。</summary>
    [Fact]
    public void ResolveUserDataFolder_CustomWithoutUserDataFolderThrows()
    {
        var options = new WebViewShellOptions { UserDataFolderRoot = UserDataFolderLocation.Custom };
        Assert.Throws<InvalidOperationException>(options.ResolveUserDataFolder);
    }

    /// <summary>应用名里的非法文件名字符必须被替换，避免创建失败。</summary>
    [Theory]
    [InlineData("My:App*Name?")]
    [InlineData("   ")]
    [InlineData("...")]
    public void ResolveUserDataFolder_SanitizesInvalidNames(string applicationName)
    {
        var options = new WebViewShellOptions { ApplicationFolderName = applicationName };
        var folder = options.ResolveUserDataFolder();

        var leaf = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
        Assert.False(string.IsNullOrWhiteSpace(leaf));
        Assert.DoesNotContain(':', leaf);
        Assert.DoesNotContain('*', leaf);
        Assert.DoesNotContain('?', leaf);
    }

    /// <summary>默认目录名保持稳定，避免升级后指向新目录导致缓存与登录态丢失。</summary>
    [Fact]
    public void DefaultProductFolderName_IsStable() =>
        Assert.Equal("BrowserShell", WebViewShellOptions.DefaultProductFolderName);
}

