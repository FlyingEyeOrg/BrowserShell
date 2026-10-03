using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BrowserShell.WebView.Wpf;

/// <summary>提供 BrowserShell 自带且可跨窗口安全复用的默认图标。</summary>
internal static class WindowIconResources
{
    private static readonly object Gate = new();
    private static ImageSource? _default;

    public static ImageSource Default
    {
        get
        {
            lock (Gate)
            {
                return _default ??= LoadDefault();
            }
        }
    }

    private static BitmapImage LoadDefault()
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri("pack://application:,,,/Assets/BrowserShell.ico", UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
