using System.Runtime.InteropServices;

namespace BrowserShell.WebView.Wpf;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct NativeMonitorInfo
{
    internal int Size;
    internal NativeRectangle Monitor;
    internal NativeRectangle WorkArea;
    internal uint Flags;
}
