using System.Text.RegularExpressions;

namespace BrowserShell.Service.SDK;

internal static partial class WindowTitleBarOptionsResolver
{
    internal static readonly WindowTitleBarOptions Defaults = new()
    {
        ActiveBackground = "#181818",
        ActiveForeground = "#CCCCCC",
        InactiveBackground = "#1F1F1F",
        InactiveForeground = "#9D9D9D",
        Border = "#2B2B2B",
    };

    internal static WindowTitleBarOptions Resolve(
        WindowTitleBarOptions? view,
        WindowTitleBarOptions? instance)
    {
        Validate(view, nameof(view));
        Validate(instance, nameof(instance));
        return new WindowTitleBarOptions
        {
            ActiveBackground = instance?.ActiveBackground ?? view?.ActiveBackground ?? Defaults.ActiveBackground,
            ActiveForeground = instance?.ActiveForeground ?? view?.ActiveForeground ?? Defaults.ActiveForeground,
            InactiveBackground = instance?.InactiveBackground ?? view?.InactiveBackground ?? Defaults.InactiveBackground,
            InactiveForeground = instance?.InactiveForeground ?? view?.InactiveForeground ?? Defaults.InactiveForeground,
            Border = instance?.Border ?? view?.Border ?? Defaults.Border,
        };
    }

    internal static void Validate(WindowTitleBarOptions? options, string parameterName)
    {
        if (options is null) return;
        foreach (var color in new[]
                 {
                     options.ActiveBackground,
                     options.ActiveForeground,
                     options.InactiveBackground,
                     options.InactiveForeground,
                     options.Border,
                 })
        {
            if (color is not null && !ColorPattern().IsMatch(color))
                throw new ArgumentException("标题栏颜色必须使用 #RRGGBB 或 #RRGGBBAA 格式。", parameterName);
        }
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?$")]
    private static partial Regex ColorPattern();
}
