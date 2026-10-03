namespace BrowserShell.Service.SDK;

/// <summary>BrowserShell 窗口核心向宿主发布可选诊断/可靠事件的边界。</summary>
internal interface IPageWindowEventSink
{
    WindowEventPublishResult Publish(string topic, object data);
}
