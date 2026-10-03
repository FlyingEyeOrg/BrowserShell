using System.Text.Json;

namespace BrowserShell.Service.SDK;

/// <summary>页面在窗口保持 Pending 时提交的一次非终态动作。</summary>
internal sealed record WindowActionSubmission(
    string RequestId,
    string WindowId,
    long ExpectedRevision,
    string Action,
    JsonElement? Data);
