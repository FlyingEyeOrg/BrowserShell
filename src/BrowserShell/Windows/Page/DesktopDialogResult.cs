namespace BrowserShell;

/// <summary>Desktop 页面提交或用户关闭窗口后的强类型结果。</summary>
public sealed record DialogResult<TResult>(WindowEndState EndState, string? Action, TResult? Result);
