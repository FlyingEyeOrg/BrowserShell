using System.Text.Json;
using BrowserShell.Service.SDK;

namespace BrowserShell.Service.SDK;

internal interface IWindowLifecycleRegistration : IDisposable
{
    Task<PageWindowInteractionDecision> SubmitAsync(
        StoredWindow window,
        string? action,
        JsonElement? result,
        CancellationToken cancellationToken);

    Task<PageWindowInteractionDecision> CloseAsync(
        StoredWindow window,
        string? action,
        PageWindowCloseSource source,
        CancellationToken cancellationToken);

    Task<WindowActionAck> ActionAsync(
        StoredWindow window,
        WindowActionSubmission submission,
        CancellationToken cancellationToken);

    Task PresentedAsync(
        StoredWindow window,
        DateTimeOffset presentedAt,
        CancellationToken cancellationToken);

    Task ResolvedAsync(
        StoredWindow window,
        PageWindowEndState endState,
        string? action,
        JsonElement? result,
        CancellationToken cancellationToken);
}
