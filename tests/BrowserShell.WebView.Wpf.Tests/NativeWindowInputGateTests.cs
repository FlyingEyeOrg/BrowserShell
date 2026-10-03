namespace BrowserShell.WebView.Wpf.Tests;

public sealed class NativeWindowInputGateTests
{
    [Fact]
    public void ModalReferences_SwitchNativeStateOnlyOnZeroOneTransitions()
    {
        var nativeEnabled = true;
        var switches = new List<bool>();
        var gate = CreateGate(() => nativeEnabled, enabled =>
        {
            switches.Add(enabled);
            nativeEnabled = enabled;
        });

        gate.AddModalReference();
        gate.AddModalReference();
        gate.RemoveModalReference();
        gate.RemoveModalReference();
        gate.RemoveModalReference();

        Assert.Equal([false, true], switches);
        Assert.Equal(0, gate.ModalReferenceCount);
        Assert.False(gate.IsInputBlocked);
    }

    [Fact]
    public void ResultPending_AndModalReference_MustBothClearBeforeNativeInputReturns()
    {
        var nativeEnabled = true;
        var switches = new List<bool>();
        var gate = CreateGate(() => nativeEnabled, enabled =>
        {
            switches.Add(enabled);
            nativeEnabled = enabled;
        });

        gate.SetResultPending(true);
        gate.AddModalReference();
        gate.SetResultPending(false);
        gate.RemoveModalReference();

        Assert.Equal([false, true], switches);
        Assert.False(gate.IsInputBlocked);
    }

    [Fact]
    public void Synchronize_AppliesPendingBlockAfterHandleCreation()
    {
        var handle = IntPtr.Zero;
        var nativeEnabled = true;
        var switches = new List<bool>();
        var gate = new NativeWindowInputGate(
            "TestWindow",
            () => "window-1",
            () => handle,
            _ => nativeEnabled,
            (_, enabled) =>
            {
                switches.Add(enabled);
                nativeEnabled = enabled;
            });

        gate.AddModalReference();
        handle = new IntPtr(42);
        gate.Synchronize("HandleCreated");
        gate.Synchronize("RepeatedSynchronization");

        Assert.Equal([false], switches);
    }

    [Fact]
    public void NativeHwndBlock_KeepsWpfAndWebViewVisualTreeEnabled()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var webView = new Microsoft.Web.WebView2.Wpf.WebView2();
                var window = new System.Windows.Window { Content = webView };
                var handle = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
                var gate = new NativeWindowInputGate(
                    "IntegrationWindow",
                    () => "window-1",
                    () => handle);

                gate.AddModalReference();

                Assert.False(NativeWindowMethods.IsWindowEnabled(handle));
                Assert.True(window.IsEnabled);
                Assert.True(webView.IsEnabled);
                Assert.Same(webView, window.Content);

                gate.RemoveModalReference();

                Assert.True(NativeWindowMethods.IsWindowEnabled(handle));
                Assert.Same(webView, window.Content);
                window.Close();
                webView.Dispose();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }

    private static NativeWindowInputGate CreateGate(Func<bool> isEnabled, Action<bool> enable) =>
        new(
            "TestWindow",
            () => "window-1",
            () => new IntPtr(42),
            _ => isEnabled(),
            (_, enabled) => enable(enabled));
}
