using System.Text.Json;

namespace BrowserShell.Service.SDK;

/// <summary>标准 WebView2 对话框的便捷入口。</summary>
public static class StandardDialogExtensions
{
    public static Task<DialogResult<JsonElement>> ShowStandardDialogAsync(
        this IPageWindowService windows,
        StandardDialogRequest request,
        WindowOptions? options = null,
        CancellationToken cancellationToken = default) =>
        windows.ShowDialogAsync<StandardDialogRequest, JsonElement>(
            StandardViews.Dialog, request, options, cancellationToken);

    public static Task<WindowHandle> ShowStandardModalAsync(
        this IPageWindowService windows,
        StandardDialogRequest request,
        WindowOptions? options = null,
        CancellationToken cancellationToken = default) =>
        windows.ShowModalAsync(StandardViews.Dialog, request, options, cancellationToken);

    public static Task<WindowHandle> ShowStandardAsync(
        this IPageWindowService windows,
        StandardDialogRequest request,
        WindowOptions? options = null,
        CancellationToken cancellationToken = default) =>
        windows.ShowAsync(StandardViews.Dialog, request, options, cancellationToken);
}
