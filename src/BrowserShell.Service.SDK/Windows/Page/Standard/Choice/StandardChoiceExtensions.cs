namespace BrowserShell.Service.SDK;

public static class StandardChoiceExtensions
{
    public static Task<DialogResult<StandardChoiceResult>> ShowStandardChoiceAsync(
        this IPageWindowService windows,
        StandardChoiceRequest request,
        PageWindowOptions? options = null,
        CancellationToken cancellationToken = default) =>
        windows.ShowDialogAsync<StandardChoiceRequest, StandardChoiceResult>(
            StandardViews.Choice,
            request,
            options,
            cancellationToken);
}
