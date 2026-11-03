using System.Net;
using System.Text;

namespace BrowserShell.WebView.Wpf.Sample;

/// <summary>
/// 示例自带的本地 HTTP 服务：提供一个「响应永不结束」的端点，用于模拟
/// 服务端把响应挂住、浏览器侧 <c>body.onload</c> 永不触发的场景——
/// 即 2.1.0「窗口永久停在正在加载…」的最小复现条件。
/// </summary>
/// <remarks>
/// <para><c>GET /hang</c>：立即返回 200 与响应头，然后<b>故意不写完响应体也不关闭连接</b>，
/// 客户端会一直等待。这正是"服务端日志全 200、Chrome 里页面正常（主文档早已完成）、
/// 但外壳遮罩不消失"的等价复现。</para>
/// <para><c>GET /done</c> 或其他路径：正常返回，用于对照。</para>
/// <para>仅监听回环地址，进程退出时随进程一起消失。</para>
/// </remarks>
internal static class HangServer
{
    private const string Prefix = "http://127.0.0.1:8791/";
    private static readonly object Gate = new();
    private static HttpListener? _listener;

    /// <summary>启动服务（幂等）。端口被占用时静默跳过——很可能就是上一个示例进程还开着。</summary>
    public static void EnsureStarted()
    {
        lock (Gate)
        {
            if (_listener is not null) return;
            var listener = new HttpListener();
            listener.Prefixes.Add(Prefix);
            try
            {
                listener.Start();
            }
            catch (Exception)
            {
                // 端口占用等情况下不阻断示例：悬挂 iframe 会加载失败，
                // 但页面主体与正常 iframe 仍然可演示。
                return;
            }

            _listener = listener;
            _ = Task.Run(AcceptLoopAsync);
        }
    }

    private static async Task AcceptLoopAsync()
    {
        var listener = _listener!;
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception)
            {
                return; // listener 已停止
            }

            _ = Task.Run(() => HandleAsync(context));
        }
    }

    private static async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";
            if (path == "/hang")
            {
                // 关键：先发响应头（模拟"服务端已收到请求并回 200"），
                // 再故意不调用 Close——响应永不结束，客户端的 onload 因此永不触发。
                context.Response.StatusCode = 200;
                context.Response.ContentType = "text/html; charset=utf-8";
                var head = Encoding.UTF8.GetBytes("<!DOCTYPE html><html><body>hang");
                await context.Response.OutputStream.WriteAsync(head);
                await context.Response.OutputStream.FlushAsync();
                // 不 Close、不写结束标记。持有引用以防被 GC 提前回收。
                GC.KeepAlive(context.Response);
                return;
            }

            // 对照端点：正常完成。
            var body = Encoding.UTF8.GetBytes(
                "<!DOCTYPE html><html><body style='font:14px Segoe UI'>normal response</body></html>");
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body);
        }
        catch (Exception)
        {
            // 客户端提前断开等，属正常。
        }
        finally
        {
            try { context.Response.Close(); } catch { /* /hang 永不走到这里 */ }
        }
    }
}
