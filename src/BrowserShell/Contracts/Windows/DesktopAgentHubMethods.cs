namespace BrowserShell;

/// <summary>接入服务调用 BrowserShell Hub 的稳定方法名。</summary>
internal static class BrowserShellHubMethods
{
    public const string RegisterClientAsync = nameof(RegisterClientAsync);

    public const string SynchronizeAsync = nameof(SynchronizeAsync);

    public const string UpsertWindowAsync = nameof(UpsertWindowAsync);

    public const string ActivateWindowAsync = nameof(ActivateWindowAsync);

    public const string CloseWindowAsync = nameof(CloseWindowAsync);

    public const string SynchronizeBrowserWindowsAsync = nameof(SynchronizeBrowserWindowsAsync);

    public const string UpsertBrowserWindowAsync = nameof(UpsertBrowserWindowAsync);

    public const string ExecuteBrowserWindowCommandAsync = nameof(ExecuteBrowserWindowCommandAsync);

    public const string HeartbeatAsync = nameof(HeartbeatAsync);
}
