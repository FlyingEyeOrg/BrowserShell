using System.Text.Json;

namespace BrowserShell.Service.SDK;

/// <summary>接入服务对页面非终态动作的处理结果。</summary>
internal sealed record WindowActionAck(
    string RequestId,
    bool Accepted,
    JsonElement? Result,
    string? Error = null,
    string? ErrorCode = null,
    JsonElement? ErrorData = null);
