namespace BrowserShell;

/// <summary>标准 WebView2 对话框支持的纯文本内容与简单操作。</summary>
public sealed record StandardDialogRequest(
    string Message,
    IReadOnlyList<StandardDialogAction> Actions,
    string? Title = null,
    string? Details = null,
    StandardSeverity Severity = StandardSeverity.Information,
    bool AllowClose = true);
