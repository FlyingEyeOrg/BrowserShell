using System.Runtime.InteropServices;

namespace BrowserShell.WebView.Wpf;

/// <summary>窗口输入门控所需的少量 Win32 调用。</summary>
internal static class NativeInput
{
    /// <summary>查询窗口是否接收鼠标与键盘输入。</summary>
    internal static bool IsWindowEnabled(IntPtr handle) => PInvokeIsWindowEnabled(handle);

    /// <summary>启用或禁用窗口的输入。</summary>
    internal static bool EnableWindow(IntPtr handle, bool enable) => PInvokeEnableWindow(handle, enable);

    [DllImport("user32.dll", EntryPoint = "IsWindowEnabled", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PInvokeIsWindowEnabled(IntPtr handle);

    [DllImport("user32.dll", EntryPoint = "EnableWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PInvokeEnableWindow(IntPtr handle, [MarshalAs(UnmanagedType.Bool)] bool enable);
}
