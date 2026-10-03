namespace BrowserShell.Service.SDK;

public interface IBrowserWindowService
{
    Task<BrowserWindowHandle> OpenAsync(
        BrowserWindowOptions options,
        BrowserWindowLifecycle? lifecycle = null,
        CancellationToken cancellationToken = default);
}
