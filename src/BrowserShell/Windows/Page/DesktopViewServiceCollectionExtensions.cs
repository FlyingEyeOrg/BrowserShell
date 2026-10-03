using Microsoft.Extensions.DependencyInjection;

namespace BrowserShell;

/// <summary>Desktop View 的依赖注入注册入口。</summary>
public static class ViewServiceCollectionExtensions
{
    public static IServiceCollection AddView(
        this IServiceCollection services,
        string name,
        string path,
        Action<ViewRegistrationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!path.StartsWith('/'))
        {
            throw new ArgumentException("Desktop View 路径必须以 / 开头。", nameof(path));
        }

        var options = new ViewRegistrationOptions();
        configure?.Invoke(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Version);
        ArgumentOutOfRangeException.ThrowIfNegative(options.PrewarmWindowCount);
        ValidateIconPath(options.IconPath, nameof(options.IconPath));
        WindowTitleBarOptionsResolver.Validate(options.TitleBar, nameof(options.TitleBar));

        services.AddSingleton(new ViewDefinition(
            name,
            path,
            options.Version,
            options.PrewarmWindowCount,
            options.ReuseMode,
            options.IconPath)
        {
            TitleBar = options.TitleBar,
        });
        return services;
    }

    private static void ValidateIconPath(string? iconPath, string parameterName)
    {
        if (iconPath is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(iconPath)
            || !iconPath.StartsWith('/')
            || iconPath.StartsWith("//", StringComparison.Ordinal)
            || iconPath.Contains('?')
            || iconPath.Contains('#'))
        {
            throw InvalidIconPath(parameterName);
        }

        var extension = Path.GetExtension(iconPath);
        if (!extension.Equals(".ico", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidIconPath(parameterName);
        }
    }

    private static ArgumentException InvalidIconPath(string parameterName) =>
        new(
            "Desktop 图标必须是以单个 / 开头的服务同源 .ico 或 .png 路径。",
            parameterName);
}
