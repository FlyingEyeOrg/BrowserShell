using System.Runtime.InteropServices;

namespace BrowserShell.WebView.Wpf;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativePoint(int x, int y)
{
    internal readonly int X = x;
    internal readonly int Y = y;
}
