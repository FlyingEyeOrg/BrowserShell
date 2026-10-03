namespace BrowserShell.Service.SDK;

public sealed record StandardInputRequest(
    string Title,
    string? Description,
    IReadOnlyList<StandardInputField> Fields,
    string SubmitText = "确定",
    string CancelText = "取消");
