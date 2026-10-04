namespace BrowserShell.Service.SDK;

public sealed record StandardInputField(
    string Key,
    string Label,
    StandardInputType Type = StandardInputType.Text,
    string? InitialValue = null,
    string? Placeholder = null,
    bool Required = false,
    int? MaxLength = null,
    decimal? Minimum = null,
    decimal? Maximum = null);
