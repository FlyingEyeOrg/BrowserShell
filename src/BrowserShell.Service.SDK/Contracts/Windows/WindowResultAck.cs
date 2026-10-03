using System.Text.Json;

namespace BrowserShell.Service.SDK;

/// <summary>接入服务对窗口终态提交的权威裁决。</summary>
internal sealed record WindowResultAck(
    string SubmissionId,
    string Status,
    string State,
    long Revision,
    string? Error = null,
    string? ErrorCode = null,
    JsonElement? ErrorData = null);
