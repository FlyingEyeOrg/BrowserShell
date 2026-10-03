namespace BrowserShell;

internal sealed class ViewCatalog : IViewCatalog
{
    private readonly Dictionary<string, ViewDefinition> _viewsByName;

    public ViewCatalog(
        IEnumerable<ViewDefinition> registeredViews,
        bool includeStandardViews)
    {
        ArgumentNullException.ThrowIfNull(registeredViews);
        var views = registeredViews.ToList();
        if (includeStandardViews)
        {
            views.AddRange(StandardViews.GetDefaults());
        }

        var viewsByName = new Dictionary<string, ViewDefinition>(StringComparer.Ordinal);
        foreach (var view in views)
        {
            if (!viewsByName.TryAdd(view.Name, view))
            {
                throw new InvalidOperationException($"Desktop View {view.Name} 重复注册。");
            }
        }

        Views = views.AsReadOnly();
        _viewsByName = viewsByName;
    }

    public IReadOnlyList<ViewDefinition> Views { get; }

    public ViewDefinition GetRequired(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _viewsByName.TryGetValue(name, out var view)
            ? view
            : throw new ArgumentException($"Desktop View {name} 未注册。", nameof(name));
    }
}
