using System.IO;

namespace BrowserShell.WebView.Wpf;

/// <summary>
/// WebView2 用户数据目录的配置。可由
/// <see cref="WebViewWpfServiceCollectionExtensions.AddBrowserShellWebView(Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Action{WebViewShellOptions}?)"/>
/// 登记（配合 <c>IOptions&lt;WebViewShellOptions&gt;</c>），或直接传给
/// <see cref="WebViewShell.CreateAsync(System.Windows.Threading.Dispatcher, WebViewShellOptions?, CancellationToken)"/>。
/// </summary>
/// <remarks>
/// <para>所有属性都有合理默认值，最常见的用法是完全不配置：</para>
/// <code>
/// services.AddBrowserShellWebView();
/// </code>
///
/// <para>目录由三段拼成，各属性的职责一一对应：</para>
/// <code>
/// 最终路径 = UserDataFolderRoot \ ProductFolderName \ ApplicationFolderName
/// 例：C:\Users\me\AppData\Local\BrowserShell\SoftwareHub
///       └─ UserDataFolderRoot ─┘ └Product┘ └─ Application ─┘
/// </code>
///
/// <para><see cref="UserDataFolder"/> 是上述三段拼完的<b>完整路径</b>：一旦设置，
/// 其余三个属性全部被忽略。</para>
/// </remarks>
public sealed class WebViewShellOptions
{
    /// <summary><see cref="ProductFolderName"/> 的默认值。</summary>
    public const string DefaultProductFolderName = "BrowserShell";

    /// <summary>
    /// 固定 WebView2 Runtime 目录。为 <c>null</c>（默认）时使用系统安装的 Evergreen Runtime。
    /// </summary>
    /// <remarks>
    /// 仅在需要锁定某个 Runtime 版本（例如随应用分发的 Fixed Version）时才设置。
    /// 与用户数据目录无关，不影响下面任何路径属性。
    /// </remarks>
    public string? BrowserExecutableFolder { get; set; }

    /// <summary>
    /// 用户数据目录的<b>完整路径</b>。设置后完全接管，
    /// <see cref="UserDataFolderRoot"/>、<see cref="ProductFolderName"/>、
    /// <see cref="ApplicationFolderName"/> 全部被忽略。
    /// </summary>
    /// <remarks>
    /// <para>适合把缓存放到指定磁盘（例如换到 SSD）或企业统一管理的目录。</para>
    /// <para>该目录<b>不会被本库删除</b>。</para>
    /// </remarks>
    public string? UserDataFolder { get; set; }

    /// <summary>
    /// 用户数据目录的<b>根位置</b>，即拼接的第一段。
    /// 默认 <see cref="UserDataFolderLocation.LocalApplicationData"/>。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不放 <c>%TEMP%</c></b>：该目录里存的是 Cookie、localStorage、缓存等
    /// <b>需要长期保留</b>的数据。实测 <c>%TEMP%</c> 的 ACL 对本机其他用户与沙箱进程是
    /// <c>Modify</c>，而 <c>%LOCALAPPDATA%</c> 仅当前用户可写；此外磁盘清理、Storage Sense
    /// 与各类清理工具都会删除临时文件，一旦删除即登录态全丢。</para>
    /// </remarks>
    public UserDataFolderLocation UserDataFolderRoot { get; set; } =
        UserDataFolderLocation.LocalApplicationData;

    /// <summary>
    /// 拼接的<b>第二段</b>：产品（或厂商）目录名。默认 <see cref="DefaultProductFolderName"/>。
    /// </summary>
    /// <remarks>
    /// <para>用来区分同一台机器上的不同产品。单个产品出货时通常无需修改。</para>
    /// <para>非法文件名字符会被替换为下划线。</para>
    /// </remarks>
    public string ProductFolderName { get; set; } = DefaultProductFolderName;

    /// <summary>
    /// 拼接的<b>第三段</b>：应用目录名。为 <c>null</c>（默认）时取当前进程的可执行文件名。
    /// </summary>
    /// <remarks>
    /// <para><b>建议显式设置</b>（例如 <c>"SoftwareHub"</c>）：默认按 exe 文件名命名时，
    /// 一旦重命名可执行文件（或改出单文件版、换安装路径名）就会指向全新目录，
    /// 旧目录失去引用而永久残留，缓存与登录态也随之"丢失"。</para>
    /// <para>非法文件名字符会被替换为下划线。</para>
    /// </remarks>
    public string? ApplicationFolderName { get; set; }

    /// <summary>
    /// 解析实际使用的用户数据目录完整路径。
    /// </summary>
    /// <remarks>
    /// <para>优先级：<see cref="UserDataFolder"/>（显式指定，原样返回）＞
    /// <see cref="UserDataFolderRoot"/> + <see cref="ProductFolderName"/>
    /// + <see cref="ApplicationFolderName"/>。</para>
    /// <para>结果形如 <c>%LOCALAPPDATA%\BrowserShell\SoftwareHub</c>。</para>
    /// </remarks>
    public string ResolveUserDataFolder()
    {
        if (!string.IsNullOrWhiteSpace(UserDataFolder))
        {
            return Path.GetFullPath(UserDataFolder);
        }

        var root = UserDataFolderRoot switch
        {
            UserDataFolderLocation.LocalApplicationData =>
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            UserDataFolderLocation.RoamingApplicationData =>
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            UserDataFolderLocation.Temp => Path.GetTempPath(),
            UserDataFolderLocation.Custom => throw new InvalidOperationException(
                "UserDataFolderRoot 为 Custom 时必须设置 UserDataFolder。"),
            _ => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        };

        // LOCALAPPDATA / APPDATA 本身已按用户隔离，无需再拼用户摘要。
        var product = Sanitize(ProductFolderName);
        var application = Sanitize(
            string.IsNullOrWhiteSpace(ApplicationFolderName)
                ? GetProcessName()
                : ApplicationFolderName);

        return Path.GetFullPath(Path.Combine(root, product, application));
    }

    /// <summary>取当前进程的可执行文件名（不含扩展名），失败时退回 <c>"app"</c>。</summary>
    private static string GetProcessName()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
            {
                return Path.GetFileNameWithoutExtension(path);
            }
        }
        catch (Exception)
        {
            // 取不到就用通用名，不影响可用性。
        }

        return "app";
    }

    /// <summary>把任意字符串中的非法文件名字符替换为下划线，并限制长度。</summary>
    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        }

        var cleaned = builder.ToString().Trim().Trim('.');
        if (cleaned.Length == 0) cleaned = "app";
        return cleaned.Length <= 40 ? cleaned : cleaned[..40];
    }
}

/// <summary>用户数据目录的根位置（拼接路径的第一段）。</summary>
public enum UserDataFolderLocation
{
    /// <summary>
    /// <c>%LOCALAPPDATA%</c>。<b>默认值</b>：仅当前用户可访问，且不会被磁盘清理删除。
    /// </summary>
    LocalApplicationData,

    /// <summary>
    /// <c>%APPDATA%</c>（漫游）。仅在确实需要随域漫游配置时使用——
    /// WebView2 的缓存体积较大，漫游会显著拖慢登录。
    /// </summary>
    RoamingApplicationData,

    /// <summary>
    /// <c>%TEMP%</c>。仅用于自动化测试等「每次都要干净环境」的场景：
    /// 该位置会被系统清理，登录态不保证保留。
    /// </summary>
    Temp,

    /// <summary>
    /// 由 <see cref="WebViewShellOptions.UserDataFolder"/> 完全指定。
    /// </summary>
    Custom,
}
