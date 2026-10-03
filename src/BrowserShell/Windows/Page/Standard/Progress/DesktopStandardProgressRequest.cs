namespace BrowserShell;

public sealed record StandardProgressRequest(
    string Title,
    string? Message = null,
    bool Indeterminate = true,
    int? Percent = null,
    string? Stage = null,
    string? Details = null,
    bool CanRequestCancellation = false,
    string CancelText = "取消任务");
