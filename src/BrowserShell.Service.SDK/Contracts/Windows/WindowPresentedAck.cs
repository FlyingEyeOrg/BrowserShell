namespace BrowserShell.Service.SDK;

/// <summary>服务对首次显示报告的幂等确认。</summary>
internal sealed record WindowPresentedAck(string WindowId, bool Accepted, long Revision);
