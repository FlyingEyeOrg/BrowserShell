using System.Globalization;
using System.IO;
using System.Windows.Threading;
using BrowserShell.WebView.Wpf;
using Xunit;

namespace BrowserShell.WebView.Wpf.Tests;

/// <summary>
/// 默认 WebView2 用户数据目录的复用策略。
/// </summary>
/// <remarks>
/// <para>这一组断言锁定一个性能修复，而非纯风格问题。实测（WebView2 Runtime 157）：
/// 当用户数据根目录是<b>全新</b>的时候，<c>EnsureCoreWebView2Async</c> 约有 50% 概率
/// 空转 <b>5.2 秒</b>（期间 CPU、内存完全不变）；复用同一个根目录、只新建 profile 时，
/// 稳定在 250~290 毫秒。</para>
///
/// <para>早期实现把随机 GUID 与 PID 拼进路径，等于每个进程都是全新目录，
/// 门户每次开窗都有一半概率慢 5 秒。若有人把路径改回含 PID/GUID，这些测试会失败。</para>
/// </remarks>
public sealed class WebViewWindowEnvironmentTests
{
    /// <summary>
    /// 连续创建两个环境必须指向同一个用户数据目录——这是跨进程复用的前提。
    /// </summary>
    [Fact]
    public async Task Create_DefaultUserDataFolderIsStable()
    {
        await RunOnStaAsync(async () =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;

            var first = WebViewWindowEnvironment.Create(dispatcher);
            var second = WebViewWindowEnvironment.Create(dispatcher);
            try
            {
                Assert.Equal(first.UserDataFolder, second.UserDataFolder);
            }
            finally
            {
                await first.DisposeAsync();
                await second.DisposeAsync();
            }
        });
    }

    /// <summary>
    /// 目录名不得包含 PID 或随机 GUID：那会让每次进程启动都变成"全新目录"，
    /// 从而重新引入 5 秒空转。
    /// </summary>
    [Fact]
    public async Task Create_DefaultUserDataFolderDoesNotContainProcessIdOrGuid()
    {
        await RunOnStaAsync(async () =>
        {
            var environment = WebViewWindowEnvironment.Create(Dispatcher.CurrentDispatcher);
            try
            {
                var folder = Path.GetFileName(environment.UserDataFolder);

                Assert.DoesNotContain(
                    Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
                    folder,
                    StringComparison.Ordinal);

                // 形如 8C6976E5B541 的 12 位十六进制摘要属于预期（用户标识摘要），
                // 但 32 位十六进制是本类历史实现里的随机 GUID，不应再出现。
                Assert.DoesNotMatch("[0-9A-Fa-f]{32}", folder);
            }
            finally
            {
                await environment.DisposeAsync();
            }
        });
    }

    /// <summary>显式传入的目录必须原样使用，且释放时不得删除调用方的目录。</summary>
    [Fact]
    public async Task Create_ExplicitUserDataFolderIsPreservedAndNotDeleted()
    {
        var folder = Path.Combine(Path.GetTempPath(), "BrowserShell.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await RunOnStaAsync(async () =>
            {
                var environment = WebViewWindowEnvironment.Create(
                    Dispatcher.CurrentDispatcher,
                    userDataFolder: folder);
                Assert.Equal(folder, environment.UserDataFolder);
                await environment.DisposeAsync();
            });

            Assert.True(Directory.Exists(folder), "释放环境不应删除调用方提供的目录。");
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// 释放环境后默认目录必须仍然存在：复用目录正是性能收益的来源，
    /// 删掉它会让下次启动重新经历 5 秒空转。
    /// </summary>
    [Fact]
    public async Task DisposeAsync_KeepsDefaultUserDataFolder()
    {
        await RunOnStaAsync(async () =>
        {
            var environment = WebViewWindowEnvironment.Create(Dispatcher.CurrentDispatcher);
            var folder = environment.UserDataFolder;
            Assert.True(Directory.Exists(folder));

            await environment.DisposeAsync();

            Assert.True(Directory.Exists(folder), "释放环境不应删除默认用户数据目录。");
        });
    }

    /// <summary>
    /// 不同窗口必须共用同一个（默认）ProfileName——与浏览器"一个 profile 服务所有标签页"一致。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么这条重要</b>：早期实现按窗口标识派生 <c>ProfileName</c>，每开一窗就多一个
    /// <c>WV2Profile_*</c> 目录（实测每窗约 2.4 MB，且 WebView2 不回收），开窗频繁时磁盘线性增长；
    /// 新建 profile 还会显著提高 <c>EnsureCoreWebView2Async</c> 触发 5 秒空转的概率。</para>
    ///
    /// <para>若有人把 ProfileName 改回按窗口生成，本测试会失败。</para>
    /// </remarks>
    [Fact]
    public async Task CreateControllerOptions_SharesDefaultProfileAcrossWindows()
    {
        await RunOnStaAsync(async () =>
        {
            var environment = WebViewWindowEnvironment.Create(Dispatcher.CurrentDispatcher);
            try
            {
                // CreateControllerOptions 需要已初始化的 Core。
                await environment.InitializeAsync();

                var first = environment.CreateControllerOptions("window-a");
                var second = environment.CreateControllerOptions("window-b");

                // 不指定 ProfileName 时 WebView2 使用默认 profile，所有窗口共享。
                Assert.Equal(string.Empty, first.ProfileName);
                Assert.Equal(string.Empty, second.ProfileName);
                Assert.Equal(first.ProfileName, second.ProfileName);
            }
            finally
            {
                await environment.DisposeAsync();
            }
        });
    }

    /// <summary>在独立 STA 线程上执行异步回调，避免 WPF Dispatcher 与 xUnit 线程池冲突。</summary>
    private static async Task RunOnStaAsync(Func<Task> action)
    {
        Exception? failure = null;
        using var completed = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                action().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                completed.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(completed.Wait(TimeSpan.FromSeconds(30)), "STA 线程超时。");
        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException($"STA 线程内失败：{failure}");
        }

        await Task.CompletedTask;
    }
}
