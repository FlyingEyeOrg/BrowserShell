namespace BrowserShell.Service.SDK;

/// <summary>Web 服务对一次 BrowserWindow 用户关闭请求作出的权威裁决。</summary>
public sealed record BrowserWindowCloseDecision(
    bool Accepted,
    string? Code = null,
    string? Message = null)
{
    public static BrowserWindowCloseDecision Allow { get; } = new(true);

    public static BrowserWindowCloseDecision Reject(string message, string? code = null) =>
        new(false, code, message);
}
