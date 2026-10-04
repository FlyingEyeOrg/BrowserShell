namespace BrowserShell.Service.SDK;

public sealed class BrowserWindowHandle
{
    private readonly IBrowserWindowOperations _operations;

    internal BrowserWindowHandle(string windowId, IBrowserWindowOperations operations)
    {
        WindowId = windowId;
        _operations = operations;
    }

    public string WindowId { get; }

    public Task ShowAsync(CancellationToken token = default) => ExecuteAsync(BrowserWindowOperation.Show, null, token);
    public Task HideAsync(CancellationToken token = default) => ExecuteAsync(BrowserWindowOperation.Hide, null, token);
    public Task ActivateAsync(CancellationToken token = default) => ExecuteAsync(BrowserWindowOperation.Activate, null, token);
    public Task MinimizeAsync(CancellationToken token = default) => ExecuteAsync(BrowserWindowOperation.Minimize, null, token);
    public Task MaximizeAsync(CancellationToken token = default) => ExecuteAsync(BrowserWindowOperation.Maximize, null, token);
    public Task RestoreAsync(CancellationToken token = default) => ExecuteAsync(BrowserWindowOperation.Restore, null, token);
    public Task ReloadAsync(CancellationToken token = default) => ExecuteAsync(BrowserWindowOperation.Reload, null, token);
    /// <summary>重新生成一次性启动地址并导航；未配置启动地址工厂时导航到真实目标地址。</summary>
    public Task ReloadFromBootstrapAsync(CancellationToken token = default) =>
        _operations.ReloadFromBootstrapAsync(WindowId, token);
    public Task LoadUrlAsync(Uri url, CancellationToken token = default) => ExecuteAsync(BrowserWindowOperation.LoadUrl, url.ToString(), token);
    public Task SetTitleAsync(string title, CancellationToken token = default) => ExecuteAsync(BrowserWindowOperation.SetTitle, title, token);
    public Task CloseAsync(CancellationToken token = default) => ExecuteAsync(BrowserWindowOperation.Close, null, token);

    private Task ExecuteAsync(BrowserWindowOperation operation, string? value, CancellationToken token) =>
        _operations.ExecuteAsync(WindowId, operation, value, token);
}
