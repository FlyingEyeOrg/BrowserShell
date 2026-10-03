namespace BrowserShell;

/// <summary>生命周期回调共用的窗口上下文。</summary>
public abstract record WindowLifecycleContext(
    string WindowId,
    string ViewName,
    long Revision,
    IServiceProvider Services);
