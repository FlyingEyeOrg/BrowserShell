namespace BrowserShell;

/// <summary>服务端对页面提交或关闭请求的权威裁决。</summary>
public sealed record WindowInteractionDecision(
    bool Accepted,
    string? Code = null,
    string? Message = null,
    object? Data = null)
{
    public static WindowInteractionDecision Allow { get; } = new(true);

    public static WindowInteractionDecision Reject(
        string message,
        string? code = null,
        object? data = null) => new(false, code, message, data);
}
