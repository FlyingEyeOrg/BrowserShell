using System.Text.Json;

namespace BrowserShell;

public sealed record WindowActionContext(
    string WindowId,
    string ViewName,
    long Revision,
    IServiceProvider Services,
    string RequestId,
    string Action,
    JsonElement? Data)
    : WindowLifecycleContext(WindowId, ViewName, Revision, Services);
