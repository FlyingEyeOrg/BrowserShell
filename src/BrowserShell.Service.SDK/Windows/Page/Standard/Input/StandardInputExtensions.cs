namespace BrowserShell.Service.SDK;

public static class StandardInputExtensions
{
    public static Task<DialogResult<StandardInputResult>> ShowStandardInputAsync(
        this IPageWindowService windows,
        StandardInputRequest request,
        PageWindowOptions? options = null,
        CancellationToken cancellationToken = default) =>
        windows.ShowDialogAsync<StandardInputRequest, StandardInputResult>(
            StandardViews.Input,
            request,
            options,
            cancellationToken);
}
