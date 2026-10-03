namespace BrowserShell.Service.SDK;

/// <summary>服务端处理页面非终态动作后的结果。</summary>
public sealed record PageWindowActionResult(
    bool Accepted,
    object? Result = null,
    string? Code = null,
    string? Message = null,
    object? Data = null)
{
    public static PageWindowActionResult Success(object? result = null) => new(true, result);

    public static PageWindowActionResult Reject(
        string message,
        string? code = null,
        object? data = null) => new(false, null, code, message, data);
}
