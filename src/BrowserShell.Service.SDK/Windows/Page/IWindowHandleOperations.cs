namespace BrowserShell.Service.SDK;

internal interface IWindowHandleOperations
{
    Task UpdateAsync(string windowId, object? data, CancellationToken cancellationToken);

    Task ActivateAsync(string windowId, CancellationToken cancellationToken);

    Task CloseAsync(string windowId, CancellationToken cancellationToken);

    Task<DialogResult<TResult>> WaitAsync<TResult>(string windowId, CancellationToken cancellationToken);
}
