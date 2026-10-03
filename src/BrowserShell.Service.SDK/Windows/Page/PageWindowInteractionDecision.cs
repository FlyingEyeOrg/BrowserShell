namespace BrowserShell.Service.SDK;

/// <summary>服务端对页面提交或关闭请求的权威裁决。</summary>
public sealed record PageWindowInteractionDecision(
    bool Accepted,
    string? Code = null,
    string? Message = null,
    object? Data = null)
{
    public static PageWindowInteractionDecision Allow { get; } = new(true);

    public static PageWindowInteractionDecision Reject(
        string message,
        string? code = null,
        object? data = null) => new(false, code, message, data);
}
