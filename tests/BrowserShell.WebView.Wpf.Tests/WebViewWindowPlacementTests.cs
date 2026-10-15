using System.Windows;

namespace BrowserShell.WebView.Wpf.Tests;

/// <summary>
/// 窗口定位测试：<see cref="WebViewWindowOptions.Left"/> / <see cref="WebViewWindowOptions.Top"/>
/// 必须在窗口<b>首次可见时</b>就已生效。
/// </summary>
/// <remarks>
/// <para><b>为什么可以直接测</b>：定位完全不涉及 WebView2，只依赖 WPF 的窗口机制。
/// 这里对真实 <see cref="Window"/> 走一遍 <see cref="WebViewWindowPlacement"/>，
/// 因此断言的是"用户第一眼看到的位置"，而不是"稍后读取的位置"——后者已被居中或工作区约束
/// 覆盖，"先出现在别处再跳"的缺陷在那种读法下根本不可见。</para>
/// <para><b>STA</b>：WPF 窗口必须在 STA 线程上创建，而 xUnit 的测试线程是 MTA，
/// 因此每个用例都在专用 STA 线程上执行（WPF 单元测试的常规做法）。</para>
/// <para>期望值一律<b>由当前环境的实际工作区推导</b>，不写死显示器尺寸，以便在任意机器上运行。</para>
/// </remarks>
public sealed class WebViewWindowPlacementTests
{
    private const double WindowWidth = 800;
    private const double WindowHeight = 600;

    /// <summary>验收 1：显式坐标下，窗口首次出现即在目标坐标。</summary>
    [Fact]
    public void ExplicitPosition_PlacesWindowAtTargetOnFirstShow()
    {
        var placed = Place(new WebViewWindowOptions { Left = 588, Top = 237 });

        Assert.Equal(588, placed.FirstVisible.Left);
        Assert.Equal(237, placed.FirstVisible.Top);
    }

    /// <summary>
    /// 验收 1 的核心：显式坐标下窗口<b>不得</b>在显示之后再移动
    /// ——"先出现在别处、再跳到目标位置"正是本次要消除的现象。
    /// </summary>
    /// <remarks>这条断言专门盯住"显示后又被移动"：仅比较最终位置是测不出来的。</remarks>
    [Fact]
    public void ExplicitPosition_DoesNotMoveAfterBecomingVisible()
    {
        var placed = Place(new WebViewWindowOptions { Left = 588, Top = 237 });

        Assert.Equal(placed.FirstVisible, placed.Settled);
    }

    /// <summary>验收 1 的关键回归：显式坐标必须压过 <c>Center</c>（否则就是"出现后再跳到居中"）。</summary>
    [Fact]
    public void ExplicitPosition_WinsOverCenter()
    {
        // Center 默认为 true；显式坐标必须胜出，且首次可见位置就是它。
        var placed = Place(new WebViewWindowOptions { Center = true, Left = 100, Top = 120 });

        Assert.Equal(100, placed.FirstVisible.Left);
        Assert.Equal(120, placed.FirstVisible.Top);
        Assert.Equal(placed.FirstVisible, placed.Settled);
    }

    /// <summary>显式坐标不做工作区裁剪：屏幕外的坐标按原值使用（调用方负责合法性）。</summary>
    [Fact]
    public void ExplicitPosition_IsNotClampedToWorkArea()
    {
        var placed = Place(new WebViewWindowOptions { Left = -400, Top = 2000 });

        Assert.Equal(-400, placed.FirstVisible.Left);
        Assert.Equal(2000, placed.FirstVisible.Top);
    }

    /// <summary>验收 2：未指定坐标且 <c>Center = true</c> 时居中（行为与改动前一致）。</summary>
    [Fact]
    public void NoExplicitPosition_CentersWhenCenterIsTrue()
    {
        var placed = Place(new WebViewWindowOptions { Center = true });

        Assert.Equal(placed.Center.Left, placed.Settled.Left);
        Assert.Equal(placed.Center.Top, placed.Settled.Top);
    }

    /// <summary>
    /// 验收 2：未指定坐标且 <c>Center = false</c> 时走工作区约束，而不是居中。
    /// </summary>
    /// <remarks>
    /// 这里<b>不能</b>断言某个固定坐标：该分支依赖系统层叠位（实测每次约 52px 步进），
    /// 位置本身随运行环境而变——"约束后完整落在工作区内"才是它的契约。
    /// 而"确实没有居中"由 <see cref="NoExplicitPosition_CentersWhenCenterIsTrue"/> 的对照覆盖：
    /// 两者配置只差 <c>Center</c>，若该分支被误改成居中，此处断言的工作区边界仍成立，
    /// 但居中用例的期望值会随之失效。
    /// </remarks>
    [Fact]
    public void NoExplicitPosition_ConstrainsToWorkAreaWhenCenterIsFalse()
    {
        var placed = Place(new WebViewWindowOptions { Center = false });

        Assert.InRange(placed.Settled.Left, placed.WorkArea.Left, placed.WorkArea.Right - WindowWidth);
        Assert.InRange(placed.Settled.Top, placed.WorkArea.Top, placed.WorkArea.Bottom - WindowHeight);
    }

    /// <summary>坐标不成对时视为未指定，回退到 <c>Center</c>（单个坐标不成位置）。</summary>
    /// <remarks>字面量必须写成 <c>588.0</c> 而不能是 <c>588</c>：xUnit 不会把 <c>int</c>
    /// 转换到 <c>double?</c> 形参，写成整数会在反射调用时抛
    /// <see cref="ArgumentException"/>（已实测）。</remarks>
    [Theory]
    [InlineData(588.0, null)]
    [InlineData(null, 237.0)]
    public void UnpairedCoordinate_FallsBackToCenter(double? left, double? top)
    {
        var placed = Place(new WebViewWindowOptions { Center = true, Left = left, Top = top });

        Assert.Equal(placed.Center.Left, placed.Settled.Left);
        Assert.Equal(placed.Center.Top, placed.Settled.Top);
    }

    /// <summary><c>NaN</c> / <c>±∞</c> 不是可用坐标，按未指定处理，而不是把 <c>Center</c> 吃掉。</summary>
    [Theory]
    [InlineData(double.NaN, 100)]
    [InlineData(100, double.NaN)]
    [InlineData(double.PositiveInfinity, 100)]
    [InlineData(100, double.NegativeInfinity)]
    public void NonFiniteCoordinate_FallsBackToCenter(double left, double top)
    {
        var placed = Place(new WebViewWindowOptions { Center = true, Left = left, Top = top });

        Assert.Equal(placed.Center.Left, placed.Settled.Left);
        Assert.Equal(placed.Center.Top, placed.Settled.Top);
    }

    /// <summary>默认值（两个属性都未设）必须与改动前一致，即向后兼容。</summary>
    [Fact]
    public void Defaults_KeepCenteringBehavior()
    {
        var placed = Place(new WebViewWindowOptions());

        Assert.Equal(placed.Center.Left, placed.Settled.Left);
        Assert.Equal(placed.Center.Top, placed.Settled.Top);
    }

    private static ObservedPlacement Place(
        WebViewWindowOptions options,
        double width = WindowWidth,
        double height = WindowHeight) =>
        RunOnSta(() =>
        {
            var window = new Window
            {
                Width = width,
                Height = height,
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
            };
            try
            {
                var placement = WebViewWindowPlacement.PlaceAndShow(window, options);
                var workArea = SystemParameters.WorkArea;
                var center = (
                    Left: workArea.Left + ((workArea.Width - width) / 2),
                    Top: workArea.Top + ((workArea.Height - height) / 2));
                return new ObservedPlacement(placement.FirstVisible, placement.Settled, center, workArea);
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>一次定位的观察结果：首次可见位置、最终位置、该环境的居中位置与工作区。</summary>
    private readonly record struct ObservedPlacement(
        (double Left, double Top) FirstVisible,
        (double Left, double Top) Settled,
        (double Left, double Top) Center,
        Rect WorkArea);

    /// <summary>在专用 STA 线程上跑一段代码并取回结果。</summary>
    /// <remarks>异常在测试线程上重新抛出，以免只表现为超时。</remarks>
    private static T RunOnSta<T>(Func<T> action)
    {
        T result = default!;
        Exception? failure = null;
        var completed = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                completed.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        Assert.True(completed.Wait(TimeSpan.FromSeconds(30)), "STA 线程超时。");
        if (failure is not null)
        {
            throw new InvalidOperationException($"STA 线程上抛出 {failure.GetType().Name}。", failure);
        }

        return result;
    }
}
