using System.Text.Json;

namespace BrowserShell.Service.SDK;

internal sealed class PageWindowCompletionRequest
{
    public required string WindowId { get; init; }
    public required string Status { get; init; }
    public required string? Action { get; init; }
    public required JsonElement? Result { get; init; }
    public long? ExpectedRevision { get; init; }
}
