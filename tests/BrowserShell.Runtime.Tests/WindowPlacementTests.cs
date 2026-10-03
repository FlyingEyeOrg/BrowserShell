namespace BrowserShell.Runtime.Tests;

public sealed class WindowPlacementTests
{
    [Theory]
    [InlineData(96, 800)]
    [InlineData(120, 1000)]
    [InlineData(144, 1200)]
    [InlineData(168, 1400)]
    [InlineData(192, 1600)]
    [InlineData(240, 2000)]
    public void Center_ConvertsDipSizeAtTargetMonitorDpi(uint dpi, int expectedWidth)
    {
        var target = new MonitorTarget(
            new IntPtr(1),
            new NativeRectangle(-1920, 0, 1920, 2160),
            dpi);

        var result = WindowPlacement.Center(target, 800, 600);

        Assert.Equal(expectedWidth, result.Width);
        Assert.Equal(WindowPlacement.DipToPixels(600, dpi), result.Height);
        Assert.Equal(target.WorkArea.Left + (target.WorkArea.Width - result.Width) / 2, result.Left);
    }

    [Fact]
    public void Center_UsesIndependentWpfDpiAxes()
    {
        var target = new MonitorTarget(
            new IntPtr(1),
            new NativeRectangle(0, 0, 2400, 1800),
            144,
            192);

        var result = WindowPlacement.Center(target, 800, 600);

        Assert.Equal(1200, result.Width);
        Assert.Equal(1200, result.Height);
    }

    [Fact]
    public void Center_UsesOwnerAndClampsWindowInsideNegativeCoordinateWorkArea()
    {
        var target = new MonitorTarget(
            new IntPtr(1),
            new NativeRectangle(-2560, -200, 0, 1240),
            144);
        var owner = new NativeRectangle(-500, 900, 100, 1500);

        var result = WindowPlacement.Center(target, 640, 480, owner);

        Assert.Equal(960, result.Width);
        Assert.Equal(720, result.Height);
        Assert.Equal(-960, result.Left);
        Assert.Equal(520, result.Top);
        Assert.True(result.Left >= target.WorkArea.Left);
        Assert.True(result.Right <= target.WorkArea.Right);
        Assert.True(result.Top >= target.WorkArea.Top);
        Assert.True(result.Bottom <= target.WorkArea.Bottom);
    }

    [Fact]
    public void Center_ClampsOversizedWindowToWorkArea()
    {
        var target = new MonitorTarget(
            new IntPtr(1),
            new NativeRectangle(1920, 0, 3200, 720),
            120);

        var result = WindowPlacement.Center(target, 2000, 1200);

        Assert.Equal(target.WorkArea.Left, result.Left);
        Assert.Equal(target.WorkArea.Top, result.Top);
        Assert.Equal(target.WorkArea.Right, result.Right);
        Assert.Equal(target.WorkArea.Bottom, result.Bottom);
    }

    [Fact]
    public void Clamp_PreservesSizeAndMovesPartiallyVisibleWindowIntoNegativeCoordinateWorkArea()
    {
        var target = new MonitorTarget(
            new IntPtr(1),
            new NativeRectangle(-2560, -200, 0, 1240),
            144);

        var result = WindowPlacement.Clamp(
            target,
            new NativeRectangle(-400, 1100, 200, 1500));

        Assert.Equal(600, result.Width);
        Assert.Equal(400, result.Height);
        Assert.Equal(-600, result.Left);
        Assert.Equal(840, result.Top);
        Assert.Equal(0, result.Right);
        Assert.Equal(1240, result.Bottom);
    }

    [Fact]
    public void Clamp_RestrictsOversizedWindowToWorkArea()
    {
        var target = new MonitorTarget(
            new IntPtr(1),
            new NativeRectangle(1920, 48, 3200, 1080),
            120);

        var result = WindowPlacement.Clamp(
            target,
            new NativeRectangle(1500, 0, 3500, 1400));

        Assert.Equal(target.WorkArea.Left, result.Left);
        Assert.Equal(target.WorkArea.Top, result.Top);
        Assert.Equal(target.WorkArea.Right, result.Right);
        Assert.Equal(target.WorkArea.Bottom, result.Bottom);
    }
}
