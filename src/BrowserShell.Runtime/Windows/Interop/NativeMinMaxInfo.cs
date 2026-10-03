using System.Runtime.InteropServices;

namespace BrowserShell.Runtime;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeMinMaxInfo
{
    internal NativePoint Reserved;
    internal NativePoint MaxSize;
    internal NativePoint MaxPosition;
    internal NativePoint MinTrackSize;
    internal NativePoint MaxTrackSize;
}
