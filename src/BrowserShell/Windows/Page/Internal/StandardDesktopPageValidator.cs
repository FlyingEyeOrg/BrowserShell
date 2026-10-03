using System.Globalization;
using System.Text.Json;

namespace BrowserShell;

internal static class StandardDesktopPageValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void ValidateRequest(string viewName, string dataJson)
    {
        switch (viewName)
        {
            case StandardViews.Dialog:
                ValidateDialog(Deserialize<StandardDialogRequest>(dataJson));
                break;
            case StandardViews.Input:
                ValidateInputRequest(Deserialize<StandardInputRequest>(dataJson));
                break;
            case StandardViews.Choice:
                ValidateChoiceRequest(Deserialize<StandardChoiceRequest>(dataJson));
                break;
            case StandardViews.Progress:
                ValidateProgressRequest(Deserialize<StandardProgressRequest>(dataJson));
                break;
            case StandardViews.Notification:
                ValidateNotificationRequest(Deserialize<StandardNotificationRequest>(dataJson));
                break;
        }
    }

    public static void ValidateResult(
        StoredWindow window,
        string status,
        string? action,
        JsonElement? result)
    {
        if (status != "completed")
        {
            if (window.ViewName == StandardViews.Dialog
                && !Deserialize<StandardDialogRequest>(window.DataJson).AllowClose)
                throw new ArgumentException("此标准消息窗口不允许由用户关闭。");
            return;
        }

        switch (window.ViewName)
        {
            case StandardViews.Dialog:
                ValidateDialogResult(Deserialize<StandardDialogRequest>(window.DataJson), action);
                break;
            case StandardViews.Input:
                ValidateInputResult(
                    Deserialize<StandardInputRequest>(window.DataJson),
                    DeserializeResult<StandardInputResult>(result));
                break;
            case StandardViews.Choice:
                ValidateChoiceResult(
                    Deserialize<StandardChoiceRequest>(window.DataJson),
                    DeserializeResult<StandardChoiceResult>(result));
                break;
            case StandardViews.Progress:
                ValidateProgressResult(
                    Deserialize<StandardProgressRequest>(window.DataJson),
                    DeserializeResult<StandardProgressResult>(result));
                break;
            case StandardViews.Notification:
                ValidateNotificationResult(
                    Deserialize<StandardNotificationRequest>(window.DataJson),
                    DeserializeResult<StandardNotificationResult>(result));
                break;
        }
    }

    private static void ValidateDialog(StandardDialogRequest request)
    {
        Required(request.Message, "Message");
        if (request.Actions.Count > 10)
            throw new ArgumentException("标准消息窗口最多允许 10 个动作。");
        EnsureUniqueKeys(request.Actions.Select(item => item.Name), "标准消息动作");
        if (request.Actions.Count(item => item.Primary) > 1)
            throw new ArgumentException("标准消息窗口只能有一个默认动作。");
    }

    private static void ValidateDialogResult(StandardDialogRequest request, string? action)
    {
        var actions = EffectiveDialogActions(request);
        if (string.IsNullOrWhiteSpace(action)
            || !actions.Any(candidate => candidate.Name.Equals(action, StringComparison.Ordinal)))
            throw new ArgumentException("标准消息结果包含未知动作。");
    }

    private static void ValidateInputRequest(StandardInputRequest request)
    {
        Required(request.Title, "Title");
        if (request.Fields.Count is < 1 or > 32)
            throw new ArgumentException("标准输入窗口字段数必须为 1 到 32。");
        EnsureUniqueKeys(request.Fields.Select(field => field.Key), "标准输入字段");
        foreach (var field in request.Fields)
        {
            Required(field.Label, $"字段 {field.Key} Label");
            if (field.MaxLength is <= 0 or > 100_000)
                throw new ArgumentException($"字段 {field.Key} MaxLength 超出允许范围。");
            if (field.Type != StandardInputType.Number
                && (field.Minimum is not null || field.Maximum is not null))
                throw new ArgumentException($"非数字字段 {field.Key} 不能设置数值范围。");
            if (field.Minimum > field.Maximum)
                throw new ArgumentException($"字段 {field.Key} 的 Minimum 不能大于 Maximum。");
        }
    }

    private static void ValidateInputResult(
        StandardInputRequest request,
        StandardInputResult result)
    {
        var fields = request.Fields.ToDictionary(field => field.Key, StringComparer.Ordinal);
        if (result.Values.Keys.Any(key => !fields.ContainsKey(key)))
            throw new ArgumentException("标准输入结果包含未知字段。");
        foreach (var field in request.Fields)
        {
            result.Values.TryGetValue(field.Key, out var value);
            if (field.Required && string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"字段 {field.Key} 为必填字段。");
            if (value is null)
                continue;
            if (field.MaxLength is not null && value.Length > field.MaxLength)
                throw new ArgumentException($"字段 {field.Key} 超过最大长度 {field.MaxLength}。");
            if (field.Type == StandardInputType.Number)
            {
                if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                    throw new ArgumentException($"字段 {field.Key} 不是有效数字。");
                if (number < field.Minimum || number > field.Maximum)
                    throw new ArgumentException($"字段 {field.Key} 超出允许数值范围。");
            }
        }
    }

    private static void ValidateChoiceRequest(StandardChoiceRequest request)
    {
        Required(request.Title, "Title");
        if (request.Options.Count is < 1 or > 1_000)
            throw new ArgumentException("标准选择窗口选项数必须为 1 到 1000。");
        EnsureUniqueKeys(request.Options.Select(option => option.Key), "标准选择选项");
        var maximum = request.Mode == StandardChoiceMode.SingleSelection
            ? 1
            : request.MaximumSelections ?? request.Options.Count;
        if (request.MinimumSelections < 0 || maximum < request.MinimumSelections || maximum > request.Options.Count)
            throw new ArgumentException("标准选择窗口的最少/最多选择数无效。");
    }

    private static void ValidateChoiceResult(
        StandardChoiceRequest request,
        StandardChoiceResult result)
    {
        EnsureUniqueKeys(result.Keys, "标准选择结果");
        var enabled = request.Options.Where(option => !option.Disabled)
            .Select(option => option.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (result.Keys.Any(key => !enabled.Contains(key)))
            throw new ArgumentException("标准选择结果包含未知或禁用选项。");
        var maximum = request.Mode == StandardChoiceMode.SingleSelection
            ? 1
            : request.MaximumSelections ?? request.Options.Count;
        if (result.Keys.Count < request.MinimumSelections || result.Keys.Count > maximum)
            throw new ArgumentException("标准选择结果不满足选择数量约束。");
    }

    private static void ValidateProgressRequest(StandardProgressRequest request)
    {
        Required(request.Title, "Title");
        if (request.Indeterminate && request.Percent is not null)
            throw new ArgumentException("不确定进度不能同时设置 Percent。");
        if (!request.Indeterminate && request.Percent is not (>= 0 and <= 100))
            throw new ArgumentException("确定进度必须设置 0 到 100 的 Percent。");
    }

    private static void ValidateProgressResult(
        StandardProgressRequest request,
        StandardProgressResult result)
    {
        if (!result.CancellationRequested || !request.CanRequestCancellation)
            throw new ArgumentException("当前标准进度窗口不接受此取消请求。");
    }

    private static void ValidateNotificationRequest(StandardNotificationRequest request)
    {
        Required(request.Title, "Title");
        Required(request.Message, "Message");
        if (request.AutoCloseAfter is { } autoCloseAfter
            && (autoCloseAfter <= TimeSpan.Zero || autoCloseAfter > TimeSpan.FromDays(1)))
            throw new ArgumentException("标准通知自动关闭时间必须大于零且不超过一天。");
        if (request.Actions?.Count > 2)
            throw new ArgumentException("标准通知最多允许两个快捷动作。");
        EnsureUniqueKeys(request.Actions?.Select(item => item.Name) ?? [], "标准通知动作");
    }

    private static void ValidateNotificationResult(
        StandardNotificationRequest request,
        StandardNotificationResult result)
    {
        if (result.TimedOut)
        {
            if (result.Action is not null)
                throw new ArgumentException("标准通知超时结果不能同时包含动作。");
            return;
        }
        if (string.IsNullOrWhiteSpace(result.Action)
            || request.Actions is null
            || !request.Actions.Any(candidate => candidate.Name.Equals(result.Action, StringComparison.Ordinal)))
            throw new ArgumentException("标准通知结果包含未知动作。");
    }

    private static IReadOnlyList<StandardDialogAction> EffectiveDialogActions(
        StandardDialogRequest request) =>
        request.Actions.Count == 0
            ? [new StandardDialogAction("ok", "确定", Primary: true)]
            : request.Actions;

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new ArgumentException($"标准页面请求无法反序列化为 {typeof(T).Name}。");

    private static T DeserializeResult<T>(JsonElement? result) =>
        result is null
            ? throw new ArgumentException("标准页面结果不能为空。")
            : result.Value.Deserialize<T>(JsonOptions)
                ?? throw new ArgumentException($"标准页面结果无法反序列化为 {typeof(T).Name}。");

    private static void EnsureUniqueKeys(IEnumerable<string> keys, string label)
    {
        var values = keys.ToArray();
        if (values.Any(string.IsNullOrWhiteSpace)
            || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new ArgumentException($"{label} Key/Name 必须非空且唯一。");
    }

    private static void Required(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{label} 不能为空。");
    }
}
