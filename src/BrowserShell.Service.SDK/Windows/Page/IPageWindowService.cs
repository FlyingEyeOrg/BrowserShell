namespace BrowserShell.Service.SDK;

public interface IPageWindowService
{
    /// <summary>显示模态页面并异步等待页面提交或用户关闭。</summary>
    Task<DialogResult<TResult>> ShowDialogAsync<TData, TResult>(string viewName, TData data,
        PageWindowOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>显示模态页面，并在发布窗口前绑定本次显示请求独有的生命周期回调。</summary>
    Task<DialogResult<TResult>> ShowDialogAsync<TData, TResult>(string viewName, TData data,
        PageWindowOptions? options, PageWindowLifecycle<TResult> lifecycle,
        CancellationToken cancellationToken = default);

    /// <summary>创建模态窗口并立即返回句柄，不占用调用方 HTTP 请求。</summary>
    Task<PageWindowHandle> ShowModalAsync<TData>(string viewName, TData data,
        PageWindowOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>创建模态窗口，并在发布窗口前绑定本次显示请求独有的生命周期回调。</summary>
    Task<PageWindowHandle> ShowModalAsync<TData, TResult>(string viewName, TData data,
        PageWindowOptions? options, PageWindowLifecycle<TResult> lifecycle,
        CancellationToken cancellationToken = default);

    /// <summary>显示非模态页面并立即返回可操作句柄。</summary>
    Task<PageWindowHandle> ShowAsync<TData>(string viewName, TData data,
        PageWindowOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>显示非模态窗口，并在发布窗口前绑定本次显示请求独有的生命周期回调。</summary>
    Task<PageWindowHandle> ShowAsync<TData, TResult>(string viewName, TData data,
        PageWindowOptions? options, PageWindowLifecycle<TResult> lifecycle,
        CancellationToken cancellationToken = default);

    /// <summary>查询窗口当前权威状态，适用于页面错过实时事件后的补拉。</summary>
    Task<PageWindowStatus<TResult>?> GetStatusAsync<TResult>(string windowId,
        CancellationToken cancellationToken = default);
}
