namespace BrowserShell;

public sealed record StandardChoiceOption(
    string Key,
    string Text,
    string? Description = null,
    bool Disabled = false,
    bool Selected = false);
