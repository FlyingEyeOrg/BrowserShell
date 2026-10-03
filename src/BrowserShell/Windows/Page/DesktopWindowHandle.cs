namespace BrowserShell;

public sealed class WindowHandle
{
    private readonly IWindowHandleOperations _operations;

    internal WindowHandle(string windowId, IWindowHandleOperations operations)
    {
        WindowId = windowId;
        _operations = operations;
    }

    /// <summary>在当前服务进程会话及 Agent 重连期间保持不变的窗口标识。</summary>
    public string WindowId { get; }

    public Task UpdateAsync(object? data, CancellationToken cancellationToken = default) =>
        _operations.UpdateAsync(WindowId, data, cancellationToken);

    public Task ActivateAsync(CancellationToken cancellationToken = default) =>
        _operations.ActivateAsync(WindowId, cancellationToken);

    public Task CloseAsync(CancellationToken cancellationToken = default) =>
        _operations.CloseAsync(WindowId, cancellationToken);

    public Task<DialogResult<TResult>> WaitForCloseAsync<TResult>(CancellationToken cancellationToken = default) =>
        _operations.WaitAsync<TResult>(WindowId, cancellationToken);
}
