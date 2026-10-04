namespace BrowserShell.Service.SDK;

internal interface IBrowserWindowOperations
{
    Task ExecuteAsync(string windowId, BrowserWindowOperation operation, string? value, CancellationToken token);

    Task ReloadFromBootstrapAsync(string windowId, CancellationToken token);
}
