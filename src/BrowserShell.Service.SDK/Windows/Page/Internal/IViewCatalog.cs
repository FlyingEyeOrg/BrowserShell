namespace BrowserShell.Service.SDK;

internal interface IViewCatalog
{
    IReadOnlyList<ViewDefinition> Views { get; }

    ViewDefinition GetRequired(string name);
}
