using System.Runtime.InteropServices;

namespace BrowserShell.WebView.Wpf;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeTrackMouseEvent
{
    internal int Size;
    internal uint Flags;
    internal IntPtr WindowHandle;
    internal uint HoverTime;
}
