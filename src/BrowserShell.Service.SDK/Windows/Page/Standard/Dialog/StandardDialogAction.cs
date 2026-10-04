using System.Text.Json;

namespace BrowserShell.Service.SDK;

/// <summary>标准 WebView2 对话框中的一个文本按钮。</summary>
public sealed record StandardDialogAction(
    string Name,
    string Text,
    bool Primary = false,
    JsonElement? Value = null,
    bool IsCancel = false,
    bool IsDanger = false);
