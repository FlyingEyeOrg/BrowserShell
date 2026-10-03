namespace BrowserShell;

public sealed record StandardChoiceRequest(
    string Title,
    string? Description,
    StandardChoiceMode Mode,
    IReadOnlyList<StandardChoiceOption> Options,
    int MinimumSelections = 1,
    int? MaximumSelections = null,
    string SubmitText = "确定",
    string CancelText = "取消");
