using System.Threading;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using BrowserShell.WebView.Wpf;

namespace BrowserShell.WebView.Wpf.Tests;

public sealed class WindowFrameHitTestTests
{
    [Theory]
    [InlineData(96, 136, 8, 146)]
    [InlineData(120, 170, 9, 182)]
    [InlineData(144, 202, 11, 218)]
    [InlineData(192, 262, 13, 289)]
    public void MinimumTrackWidthContainsCaptionButtonsAndOneResizeBorder(
        uint dpi,
        int systemMinimum,
        int resizeBorderWidth,
        int expected)
    {
        var result = WindowFrameHitTest.CalculateMinimumTrackWidth(
            currentMinimum: 0,
            systemMinimum,
            resizeBorderWidth,
            WindowFrame.CaptionButtonWidth * 3,
            dpi);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(16)]
    public void ResizeOverlayUsesTopInsideAndOtherBordersOutside(int border)
    {
        var owner = new NativeRectangle(-1800, -200, -800, 500);
        var overlay = WindowResizeOverlay.CalculateBounds(owner, border, border);

        Assert.Equal(owner.Left - border, overlay.Left);
        Assert.Equal(owner.Top, overlay.Top);
        Assert.Equal(owner.Right + border, overlay.Right);
        Assert.Equal(owner.Bottom + border, overlay.Bottom);
        Assert.Equal(WindowFrameHitTest.TopLeft, Hit(overlay.Left, overlay.Top));
        Assert.Equal(WindowFrameHitTest.TopRight, Hit(overlay.Right - 1, overlay.Top));
        Assert.Equal(WindowFrameHitTest.BottomLeft, Hit(overlay.Left, overlay.Bottom - 1));
        Assert.Equal(WindowFrameHitTest.BottomRight, Hit(overlay.Right - 1, overlay.Bottom - 1));
        Assert.Equal(WindowFrameHitTest.Left, Hit(overlay.Left, owner.Top + 100));
        Assert.Equal(WindowFrameHitTest.Right, Hit(overlay.Right - 1, owner.Top + 100));
        Assert.Equal(WindowFrameHitTest.Top, Hit(owner.Left + 100, owner.Top));
        Assert.Equal(WindowFrameHitTest.Bottom, Hit(owner.Left + 100, overlay.Bottom - 1));
        Assert.Equal(WindowFrameHitTest.Client, Hit(owner.Left + 100, owner.Top + border));

        int Hit(int x, int y) => WindowResizeOverlay.EvaluateHit(
            new NativePoint(x, y), overlay, border, border);
    }

    [Fact]
    public void CaptionButtonsAndIconUseActualScreenRectangles()
    {
        var icon = new NativeRectangle(110, 109, 126, 125);
        var minimize = new NativeRectangle(962, 100, 1008, 135);
        var maximize = new NativeRectangle(1008, 100, 1054, 135);
        var close = new NativeRectangle(1054, 100, 1100, 135);

        Assert.Equal(WindowFrameHitTest.SystemMenu, Hit(110, 109));
        Assert.Equal(WindowFrameHitTest.MinButton, Hit(980, 125));
        Assert.Equal(WindowFrameHitTest.MaxButton, Hit(1020, 125));
        Assert.Equal(WindowFrameHitTest.Close, Hit(1080, 125));
        Assert.Equal(WindowFrameHitTest.Caption, Hit(500, 125));
        Assert.Equal(WindowFrameHitTest.Client, Hit(500, 135));

        int Hit(int x, int y) => WindowFrameHitTest.Evaluate(
            new NativePoint(x, y),
            icon, minimize, maximize, close, 135);
    }

    [Fact]
    public void MaximizedPlacementUsesWorkAreaRelativeToNegativeCoordinateMonitor()
    {
        var monitor = new NativeRectangle(-2560, -200, 0, 1240);
        var workArea = new NativeRectangle(-2560, -160, 0, 1200);

        var result = WindowFrameHitTest.CalculateMaximizedPlacement(monitor, workArea);

        Assert.Equal(0, result.Position.X);
        Assert.Equal(40, result.Position.Y);
        Assert.Equal(2560, result.Size.X);
        Assert.Equal(1360, result.Size.Y);
    }

    [Theory]
    [InlineData(0, 0, 1920, 1032)]
    [InlineData(0, 48, 1920, 1080)]
    [InlineData(48, 0, 1920, 1080)]
    [InlineData(0, 0, 1872, 1080)]
    public void MaximizedClientIsClampedToEveryTaskbarEdge(
        int workLeft,
        int workTop,
        int workRight,
        int workBottom)
    {
        var workArea = new NativeRectangle(workLeft, workTop, workRight, workBottom);
        var proposed = new NativeRectangle(workLeft - 8, workTop - 8, workRight + 8, workBottom + 8);
        var result = WindowFrameHitTest.ClampToWorkArea(proposed, workArea);

        Assert.Equal(workArea.Left, result.Left);
        Assert.Equal(workArea.Top, result.Top);
        Assert.Equal(workArea.Right, result.Right);
        Assert.Equal(workArea.Bottom, result.Bottom);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(16)]
    public void MaximizedBoundsDetectionUsesDpiScaledResizeBorder(int border)
    {
        var workArea = new NativeRectangle(1920, 0, 3840, 1032);
        var maximized = new NativeRectangle(
            workArea.Left - border,
            workArea.Top - border,
            workArea.Right + border,
            workArea.Bottom + border);

        Assert.True(WindowFrameHitTest.MatchesMaximizedBounds(maximized, workArea, border, border));
        Assert.False(WindowFrameHitTest.MatchesMaximizedBounds(
            new NativeRectangle(2000, 100, 3000, 800),
            workArea,
            border,
            border));
    }

    [Fact]
    public void RealWpfHwndKeepsCaptionAndRoutesResizeThroughExternalOverlay()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new Window
                {
                    Width = 500,
                    Height = 300,
                    Left = NativeWindowMethods.GetSystemMetrics(NativeWindowMethods.SmXVirtualScreen)
                        + NativeWindowMethods.GetSystemMetrics(NativeWindowMethods.SmCxVirtualScreen)
                        - 600,
                    Top = NativeWindowMethods.GetSystemMetrics(NativeWindowMethods.SmYVirtualScreen)
                        + NativeWindowMethods.GetSystemMetrics(NativeWindowMethods.SmCyVirtualScreen)
                        - 400,
                    Title = new string('W', 512),
                    ShowInTaskbar = false,
                    ShowActivated = false,
                };
                using var frame = new WindowFrame(window);
                var hostedContent = new Border();
                frame.Content = hostedContent;
                var root = Assert.IsType<Grid>(window.Content);
                Assert.Single(root.ColumnDefinitions);
                Assert.Equal(GridUnitType.Star, root.ColumnDefinitions[0].Width.GridUnitType);
                Assert.Equal(0, root.ColumnDefinitions[0].MinWidth);
                var titleBar = Assert.IsType<Border>(root.Children[0]);
                var titleGrid = Assert.IsType<Grid>(titleBar.Child);
                Assert.Collection(
                    titleGrid.ColumnDefinitions,
                    column => Assert.Equal(GridUnitType.Star, column.Width.GridUnitType),
                    column => Assert.True(column.Width.IsAuto));
                var titleContent = Assert.IsType<Border>(titleGrid.Children[0]);
                Assert.True(titleContent.ClipToBounds);
                var titleItems = Assert.IsType<StackPanel>(titleContent.Child);
                Assert.Equal(Orientation.Horizontal, titleItems.Orientation);
                var title = Assert.IsType<TextBlock>(titleItems.Children[1]);
                Assert.Equal(HorizontalAlignment.Left, title.HorizontalAlignment);
                Assert.Equal(default, title.Margin);
                var captionButtons = Assert.IsType<StackPanel>(titleGrid.Children[1]);
                Assert.Equal(1, Grid.GetColumn(captionButtons));
                var contentHost = Assert.IsType<ContentControl>(root.Children[1]);
                Assert.Equal(HorizontalAlignment.Stretch, contentHost.HorizontalContentAlignment);
                Assert.Equal(VerticalAlignment.Stretch, contentHost.VerticalContentAlignment);
                Assert.Equal(default, root.Margin);
                var handle = new WindowInteropHelper(window).EnsureHandle();
                window.Show();
                window.UpdateLayout();
                frame.SynchronizeResizeOverlay();
                var overlay = frame.ResizeOverlayHandle;
                Assert.NotEqual(IntPtr.Zero, overlay);
                Assert.True(NativeWindowMethods.IsWindowVisible(overlay));
                Assert.Equal(contentHost.ActualWidth, hostedContent.ActualWidth, 3);
                Assert.Equal(contentHost.ActualHeight, hostedContent.ActualHeight, 3);
                window.Width = 1;
                window.UpdateLayout();
                Assert.Equal(138, captionButtons.ActualWidth, 3);
                Assert.Equal(captionButtons.ActualWidth, titleGrid.ColumnDefinitions[1].ActualWidth, 3);
                Assert.Equal(
                    titleGrid.ActualWidth - captionButtons.ActualWidth,
                    titleGrid.ColumnDefinitions[0].ActualWidth,
                    3);
                var captionButtonsRight = captionButtons.PointToScreen(
                    new Point(captionButtons.ActualWidth, 0)).X;
                Assert.True(NativeWindowMethods.GetClientRect(handle, out var narrowClient));
                var narrowClientOrigin = new NativePoint(0, 0);
                Assert.True(NativeWindowMethods.ClientToScreen(handle, ref narrowClientOrigin));
                Assert.InRange(
                    Math.Abs(captionButtonsRight - (narrowClientOrigin.X + narrowClient.Width)),
                    0,
                    1);
                window.Width = 800;
                window.Height = 500;
                window.UpdateLayout();
                Assert.Equal(contentHost.ActualWidth, hostedContent.ActualWidth, 3);
                Assert.Equal(contentHost.ActualHeight, hostedContent.ActualHeight, 3);
                var monitor = NativeWindowMethods.MonitorFromWindow(handle, NativeWindowMethods.MonitorDefaultToNearest);
                var monitorInfo = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
                Assert.True(NativeWindowMethods.GetMonitorInfo(monitor, ref monitorInfo));
                var dpi = NativeWindowMethods.GetDpiForWindow(handle);
                if (dpi == 0) dpi = 96;
                var limitsPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMinMaxInfo>());
                try
                {
                    Marshal.StructureToPtr(default(NativeMinMaxInfo), limitsPointer, false);
                    _ = NativeWindowMethods.SendMessage(handle, 0x0024, IntPtr.Zero, limitsPointer);
                    var limits = Marshal.PtrToStructure<NativeMinMaxInfo>(limitsPointer);
                    var expected = WindowFrameHitTest.CalculateMaximizedPlacement(
                        monitorInfo.Monitor,
                        monitorInfo.WorkArea);
                    Assert.Equal(expected.Position.X, limits.MaxPosition.X);
                    Assert.Equal(expected.Position.Y, limits.MaxPosition.Y);
                    Assert.Equal(expected.Size.X, limits.MaxSize.X);
                    Assert.Equal(expected.Size.Y, limits.MaxSize.Y);
                    var expectedMinimumWidth = WindowFrameHitTest.CalculateMinimumTrackWidth(
                        currentMinimum: 0,
                        NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCxMinTrack, dpi),
                        NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCxFrame, dpi)
                            + NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCxPaddedBorder, dpi),
                        WindowFrame.CaptionButtonWidth * 3,
                        dpi);
                    Assert.True(
                        limits.MinTrackSize.X >= expectedMinimumWidth,
                        $"Native minimum width {limits.MinTrackSize.X}px must contain the caption buttons at {dpi} DPI.");
                }
                finally
                {
                    Marshal.FreeHGlobal(limitsPointer);
                }
                for (var iteration = 0; iteration < 6; iteration++)
                {
                    _ = NativeWindowMethods.SendMessage(handle, 0x0112, new IntPtr(0xF030), IntPtr.Zero);
                    DrainDispatcher();
                    window.UpdateLayout();
                    frame.SynchronizeResizeOverlay();
                    Assert.False(NativeWindowMethods.IsWindowVisible(overlay));
                    Assert.True(NativeWindowMethods.GetClientRect(handle, out var maximizedClient));
                    var clientOrigin = new NativePoint(0, 0);
                    Assert.True(NativeWindowMethods.ClientToScreen(handle, ref clientOrigin));
                    Assert.Equal(monitorInfo.WorkArea.Left, clientOrigin.X);
                    Assert.Equal(monitorInfo.WorkArea.Top, clientOrigin.Y);
                    Assert.Equal(monitorInfo.WorkArea.Right, clientOrigin.X + maximizedClient.Width);
                    Assert.Equal(monitorInfo.WorkArea.Bottom, clientOrigin.Y + maximizedClient.Height);
                    Assert.Equal(default, root.Margin);

                    var rootOrigin = root.PointToScreen(new Point());
                    var rootOpposite = root.PointToScreen(new Point(root.ActualWidth, root.ActualHeight));
                    Assert.InRange(Math.Abs(rootOrigin.X - clientOrigin.X), 0, 1);
                    Assert.InRange(Math.Abs(rootOrigin.Y - clientOrigin.Y), 0, 1);
                    Assert.InRange(
                        Math.Abs(rootOpposite.X - (clientOrigin.X + maximizedClient.Width)),
                        0,
                        1);
                    Assert.InRange(
                        Math.Abs(rootOpposite.Y - (clientOrigin.Y + maximizedClient.Height)),
                        0,
                        1);

                    var maximizedButtonsRight = captionButtons.PointToScreen(
                        new Point(captionButtons.ActualWidth, 0)).X;
                    Assert.InRange(
                        Math.Abs(maximizedButtonsRight - (clientOrigin.X + maximizedClient.Width)),
                        0,
                        1);
                    var rightEdgeHit = NativeWindowMethods.SendMessage(
                        handle,
                        0x0084,
                        IntPtr.Zero,
                        Pack(clientOrigin.X + maximizedClient.Width - 1, clientOrigin.Y + 10));
                    Assert.Equal(WindowFrameHitTest.Close, rightEdgeHit.ToInt32());

                    _ = NativeWindowMethods.SendMessage(handle, 0x0112, new IntPtr(0xF120), IntPtr.Zero);
                    DrainDispatcher();
                    window.UpdateLayout();
                    Assert.Equal(default, root.Margin);
                }
                var style = NativeWindowMethods.GetWindowLongPtr(handle, NativeWindowMethods.GwlStyle).ToInt64();
                Assert.Equal(0x00C00000L, style & 0x00C00000L);
                Assert.NotEqual(0, style & 0x00040000L);
                Assert.True(NativeWindowMethods.GetWindowRect(handle, out var bounds));
                Assert.True(NativeWindowMethods.GetClientRect(handle, out var client));
                Assert.Equal(bounds.Width, client.Width);
                Assert.Equal(bounds.Height, client.Height);
                frame.SynchronizeResizeOverlay();
                Assert.True(NativeWindowMethods.IsWindowVisible(overlay));
                Assert.True(NativeWindowMethods.GetWindowRect(overlay, out var overlayBounds));
                var resizeWidth = NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCxFrame, dpi)
                    + NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCxPaddedBorder, dpi);
                var resizeHeight = NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCyFrame, dpi)
                    + NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCxPaddedBorder, dpi);
                Assert.Equal(bounds.Left - resizeWidth, overlayBounds.Left);
                Assert.Equal(bounds.Top, overlayBounds.Top);
                Assert.Equal(bounds.Right + resizeWidth, overlayBounds.Right);
                Assert.Equal(bounds.Bottom + resizeHeight, overlayBounds.Bottom);
                var result = NativeWindowMethods.SendMessage(
                    handle,
                    0x0084,
                    IntPtr.Zero,
                    Pack(bounds.Left, bounds.Top + bounds.Height / 2));
                Assert.Equal(WindowFrameHitTest.Client, result.ToInt32());
                result = NativeWindowMethods.SendMessage(
                    overlay,
                    0x0084,
                    IntPtr.Zero,
                    Pack(overlayBounds.Left, bounds.Top + bounds.Height / 2));
                Assert.Equal(WindowFrameHitTest.Left, result.ToInt32());
                var buttonWidth = (int)Math.Round(46 * dpi / 96d);
                var buttonY = bounds.Top + Math.Max(1, resizeHeight / 2);
                result = NativeWindowMethods.SendMessage(
                    handle,
                    0x0084,
                    IntPtr.Zero,
                    Pack(bounds.Right - buttonWidth - buttonWidth / 2, buttonY));
                Assert.Equal(WindowFrameHitTest.MaxButton, result.ToInt32());
                result = NativeWindowMethods.SendMessage(
                    overlay,
                    0x0084,
                    IntPtr.Zero,
                    Pack(bounds.Right - buttonWidth - buttonWidth / 2, buttonY));
                Assert.Equal(WindowResizeOverlay.HitTransparent, result.ToInt32());
                result = NativeWindowMethods.SendMessage(
                    overlay,
                    0x0084,
                    IntPtr.Zero,
                    Pack(bounds.Left + bounds.Width / 2, bounds.Top + Math.Max(1, resizeHeight / 2)));
                Assert.Equal(WindowFrameHitTest.Top, result.ToInt32());
                _ = NativeWindowMethods.EnableWindow(handle, false);
                result = NativeWindowMethods.SendMessage(
                    overlay,
                    0x0084,
                    IntPtr.Zero,
                    Pack(overlayBounds.Left, bounds.Top + bounds.Height / 2));
                Assert.Equal(0, result.ToInt32());
                _ = NativeWindowMethods.EnableWindow(handle, true);
                window.Hide();
                Assert.False(NativeWindowMethods.IsWindowVisible(overlay));
                window.Show();
                window.UpdateLayout();
                frame.SynchronizeResizeOverlay();
                Assert.True(NativeWindowMethods.IsWindowVisible(overlay));
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "STA window test timed out.");
        Assert.Null(failure);
    }

    private static IntPtr Pack(int x, int y) =>
        new(unchecked((int)(((uint)(ushort)y << 16) | (ushort)x)));

    private static void DrainDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

}
