namespace BrowserShell.Service.SDK;

/// <summary>BrowserShell 页面提交或用户关闭窗口后的强类型结果。</summary>
public sealed record DialogResult<TResult>(PageWindowEndState EndState, string? Action, TResult? Result);
