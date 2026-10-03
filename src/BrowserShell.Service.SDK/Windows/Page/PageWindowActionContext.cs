using System.Text.Json;

namespace BrowserShell.Service.SDK;

public sealed record PageWindowActionContext(
    string WindowId,
    string ViewName,
    long Revision,
    IServiceProvider Services,
    string RequestId,
    string Action,
    JsonElement? Data)
    : PageWindowLifecycleContext(WindowId, ViewName, Revision, Services);
