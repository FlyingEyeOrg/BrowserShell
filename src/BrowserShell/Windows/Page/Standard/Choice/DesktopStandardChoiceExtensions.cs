namespace BrowserShell;

public static class StandardChoiceExtensions
{
    public static Task<DialogResult<StandardChoiceResult>> ShowStandardChoiceAsync(
        this IPageWindowService windows,
        StandardChoiceRequest request,
        WindowOptions? options = null,
        CancellationToken cancellationToken = default) =>
        windows.ShowDialogAsync<StandardChoiceRequest, StandardChoiceResult>(
            StandardViews.Choice,
            request,
            options,
            cancellationToken);
}
