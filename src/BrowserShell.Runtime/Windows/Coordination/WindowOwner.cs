using System.Windows;
using System.Windows.Interop;

namespace BrowserShell.Runtime;

internal sealed record WindowOwner(
    string WindowKind,
    string WindowId,
    Window Window,
    Action Block,
    Action Release)
{
    public IntPtr Handle => new WindowInteropHelper(Window).Handle;
}
