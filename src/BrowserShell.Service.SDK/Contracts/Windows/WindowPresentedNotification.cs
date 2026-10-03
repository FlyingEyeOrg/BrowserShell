namespace BrowserShell.Service.SDK;

/// <summary>Agent 报告一个窗口已经完成首次真实显示。</summary>
internal sealed record WindowPresentedNotification(string WindowId, long Revision, DateTimeOffset PresentedAt);
