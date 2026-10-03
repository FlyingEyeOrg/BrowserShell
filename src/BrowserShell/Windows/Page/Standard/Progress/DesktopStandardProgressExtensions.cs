namespace BrowserShell;

public static class StandardProgressExtensions
{
    public static Task<WindowHandle> ShowStandardProgressAsync(
        this IPageWindowService windows,
        StandardProgressRequest request,
        WindowOptions? options = null,
        CancellationToken cancellationToken = default) =>
        windows.ShowAsync(StandardViews.Progress, request, options, cancellationToken);

    public static Task UpdateStandardProgressAsync(
        this WindowHandle handle,
        StandardProgressRequest request,
        CancellationToken cancellationToken = default) =>
        handle.UpdateAsync(request, cancellationToken);
}
