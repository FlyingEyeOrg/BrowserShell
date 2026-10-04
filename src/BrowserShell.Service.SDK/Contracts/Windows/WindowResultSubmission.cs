using System.Text.Json;

namespace BrowserShell.Service.SDK;

/// <summary>操作员对一个窗口发起的一次即时终态提交。</summary>
internal sealed record WindowResultSubmission(
    string SubmissionId,
    string WindowId,
    long ExpectedRevision,
    DateTimeOffset NotAfterUtc,
    string State,
    string? Action,
    JsonElement? Result,
    string? Source = null);
