namespace BrowserShell;

internal interface IViewCatalog
{
    IReadOnlyList<ViewDefinition> Views { get; }

    ViewDefinition GetRequired(string name);
}
