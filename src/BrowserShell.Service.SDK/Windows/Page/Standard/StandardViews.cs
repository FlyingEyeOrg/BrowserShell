namespace BrowserShell.Service.SDK;

/// <summary>SDK 自动注册的标准 WebView2 页面名称与路径。</summary>
public static class StandardViews
{
    public const string Dialog = "softwarehub.standard-dialog";
    public const string DialogPath = "/_softwarehub/sdk/standard-dialog.html";
    public const string Input = "softwarehub.standard-input";
    public const string InputPath = "/_softwarehub/sdk/standard-input.html";
    public const string Choice = "softwarehub.standard-choice";
    public const string ChoicePath = "/_softwarehub/sdk/standard-choice.html";
    public const string Progress = "softwarehub.standard-progress";
    public const string ProgressPath = "/_softwarehub/sdk/standard-progress.html";
    public const string Notification = "softwarehub.standard-notification";
    public const string NotificationPath = "/_softwarehub/sdk/standard-notification.html";

    internal static IReadOnlyList<ViewDefinition> GetDefaults() =>
    [
        new(Dialog, DialogPath, 1, 1, ViewReuseMode.TrustedReset),
        new(Input, InputPath, 1, 1, ViewReuseMode.TrustedReset),
        new(Choice, ChoicePath, 1, 1, ViewReuseMode.TrustedReset),
        new(Progress, ProgressPath, 1, 1, ViewReuseMode.TrustedReset),
        new(Notification, NotificationPath, 1, 2, ViewReuseMode.TrustedReset),
    ];
}
