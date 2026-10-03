namespace BrowserShell;

public sealed record StandardNotificationRequest(
    string Title,
    string Message,
    StandardSeverity Severity = StandardSeverity.Information,
    TimeSpan? AutoCloseAfter = null,
    IReadOnlyList<StandardNotificationAction>? Actions = null);
