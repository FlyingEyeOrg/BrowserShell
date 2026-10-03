namespace BrowserShell;

public static class StandardNotificationExtensions
{
    public static Task<DialogResult<StandardNotificationResult>> ShowStandardNotificationAsync(
        this IPageWindowService windows,
        StandardNotificationRequest request,
        WindowOptions? options = null,
        CancellationToken cancellationToken = default) =>
        windows.ShowDialogAsync<StandardNotificationRequest, StandardNotificationResult>(
            StandardViews.Notification,
            request,
            options,
            cancellationToken);
}
