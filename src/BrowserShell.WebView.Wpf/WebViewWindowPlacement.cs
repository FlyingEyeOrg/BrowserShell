using System.Windows;
using System.Windows.Interop;
using WindowChromeKit.Wpf;

namespace BrowserShell.WebView.Wpf;

/// <summary>
/// 窗口<b>首次显示</b>时的位置决策与落地，也就是"窗口第一眼出现在哪里"。
/// </summary>
/// <remarks>
/// <para><b>为什么单独成类型</b>：位置必须在显示之前确定，而这段时序一旦写进
/// <see cref="WebViewWindow"/> 就会和 WebView2 初始化绑死，无法单独验证。
/// 独立出来后，它只依赖 WPF 的窗口机制——不需要真实 WebView2 Runtime 即可测试。</para>
///
/// <para><b>优先级</b>：显式 <see cref="WebViewWindowOptions.Left"/> +
/// <see cref="WebViewWindowOptions.Top"/> &gt; <see cref="WebViewWindowOptions.Center"/> &gt; 系统默认。</para>
///
/// <para><b>时序不可颠倒</b>：先落位、再 <c>Show()</c>。窗口一旦显示再改坐标，
/// 用户就会看到"先出现在别处、再跳到目标位置"——这正是把
/// <see cref="WebViewWindowOptions.Left"/> / <see cref="WebViewWindowOptions.Top"/>
/// 放进创建时设置、而不是打开后赋值的原因。</para>
///
/// <para><b>与 WebView2 的时序关系</b>：本类型负责的 <c>EnsureHandle()</c> 必须在创建
/// WebView2 Controller <b>之前</b>完成，否则自绘标题栏会落在 DWM 尚未接管客户区的时间点，
/// 出现绘制错位。因此调用方须在初始化 WebView2 之前调用本方法。</para>
/// </remarks>
internal static class WebViewWindowPlacement
{
    /// <summary>
    /// 在窗口显示<b>之前</b>落位，随即创建 HWND 并显示。
    /// </summary>
    /// <param name="window">尚未显示的窗口。</param>
    /// <param name="options">窗口设置。</param>
    /// <returns>
    /// <see cref="InitialPlacement.FirstVisible"/> 是窗口<b>首次变为可见那一刻</b>的坐标：
    /// 取值点紧跟 <c>Show()</c>、早于任何回退调整，因此它就是用户第一眼看到的位置
    /// （见 <see cref="InitialPlacement"/> 的说明）。
    /// </returns>
    public static InitialPlacement PlaceAndShow(Window window, WebViewWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(options);

        var explicitPosition = ResolveExplicitPosition(options);
        // WindowStartupLocation 必须【显式】设置。WPF 默认是 Manual，而 Manual 在 Left/Top
        // 未赋值时会落到系统层叠位（实测每一步约 52px 偏移，长期停在左上角附近）——
        // 这正是"窗口先出现在角落"的来源；写清楚取值，行为才不依赖 WPF 的默认值。
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        if (explicitPosition is { } position)
        {
            window.Left = position.Left;
            window.Top = position.Top;
        }

        // EnsureHandle() 必须早于 WebView2 Controller 的创建（见类型注释），
        // 且早于 Show()：先有 HWND 才有"显示"这一动作。
        _ = new WindowInteropHelper(window).EnsureHandle();
        window.Show();

        // 取值点必须在【这里】：窗口已可见，而回退调整尚未发生。
        // 若改到方法末尾（即回退之后）再读，"显示后又被移动"这一类缺陷会完全测不出来——
        // 那正是本属性要防的"先出现在别处再跳"。
        var firstVisible = (window.Left, window.Top);

        if (explicitPosition is null)
        {
            // 未指定显式坐标：沿用原有行为。注意这里保留 CenterOnTargetMonitor 而不是改用
            // WPF 的 WindowStartupLocation.CenterScreen——前者居中于"所有者（无所有者时前台窗口）
            // 所在的显示器"，多显示器下与 CenterScreen 的取屏规则不同，替换会改变现有行为。
            if (options.Center)
            {
                WindowPlacementService.CenterOnTargetMonitor(window);
            }
            else
            {
                WindowPlacementService.ConstrainToWorkArea(window);
            }
        }

        // 显式坐标下不做任何工作区裁剪：坐标按原值使用，合法性由调用方负责
        // （见 WebViewWindowOptions.Left 的说明）。
        return new InitialPlacement(firstVisible, (window.Left, window.Top));
    }

    /// <summary>
    /// 取显式坐标；<b>未成对指定</b>或<b>含非有限值</b>时返回 <c>null</c>（视为未指定）。
    /// </summary>
    /// <remarks>
    /// <para>单个坐标不成位置，故只给 <see cref="WebViewWindowOptions.Left"/> 或只给
    /// <see cref="WebViewWindowOptions.Top"/> 一律回退到 <see cref="WebViewWindowOptions.Center"/>。</para>
    /// <para><c>NaN</c> 与 <c>±∞</c> 不是可用坐标（在 WPF 里 <c>NaN</c> 恰是"未设置"的表示），
    /// 同样按未指定处理，避免一个无意义的坐标反而把 <c>Center</c> 吃掉。</para>
    /// </remarks>
    private static (double Left, double Top)? ResolveExplicitPosition(WebViewWindowOptions options) =>
        options.Left is { } left && options.Top is { } top && double.IsFinite(left) && double.IsFinite(top)
            ? (left, top)
            : null;
}

/// <summary>一次初始定位的结果。</summary>
/// <param name="FirstVisible">
/// 窗口首次变为可见那一刻的 <c>(Left, Top)</c>。这是<b>用户第一眼看到</b>的位置，
/// 也是"先出现在别处再跳"这类缺陷唯一能暴露出来的观察点。
/// </param>
/// <param name="Settled">位置调整全部结束后的 <c>(Left, Top)</c>。</param>
/// <remarks>
/// 两者在显式坐标下应当<b>相等</b>（不再有任何后续调整）；在回退分支下则可能不同——
/// 那正是需要被看见的差异，因此这里把两个时点都显式暴露出来，而不是只给一个"最终位置"。
/// </remarks>
internal readonly record struct InitialPlacement(
    (double Left, double Top) FirstVisible,
    (double Left, double Top) Settled);
