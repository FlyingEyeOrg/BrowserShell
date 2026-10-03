using System.Text.Json;
using BrowserShell.Service.SDK;

namespace BrowserShell.Service.SDK;

internal interface IWindowLifecycleRegistration : IDisposable
{
    Task<WindowInteractionDecision> SubmitAsync(
        StoredWindow window,
        string? action,
        JsonElement? result,
        CancellationToken cancellationToken);

    Task<WindowInteractionDecision> CloseAsync(
        StoredWindow window,
        string? action,
        WindowCloseSource source,
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
        WindowEndState endState,
        string? action,
        JsonElement? result,
        CancellationToken cancellationToken);
}
