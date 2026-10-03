using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using BrowserShell.Service.SDK;

namespace BrowserShell.Runtime;

/// <summary>读取并严格校验有界流式 Pending 快照。</summary>
internal sealed class WindowSnapshotAssembler
{
    private const int ProtocolVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SnapshotResourceCoordinator _resources;
    private readonly AgentWindowResourceOptions _limits;

    public WindowSnapshotAssembler(SnapshotResourceCoordinator resources, RuntimeSettings settings)
    {
        _resources = resources;
        _limits = settings.WindowResources;
    }

    public async Task<WindowSnapshot> AssembleAsync(
        IAsyncEnumerable<WindowSnapshotFrame> frames,
        string expectedLeaseId,
        CancellationToken token)
    {
        await using var enumerator = frames.GetAsyncEnumerator(token);
        if (!await enumerator.MoveNextAsync() || enumerator.Current.Kind != WindowSnapshotFrameKind.Header)
        {
            throw new InvalidDataException("快照必须以 Header 帧开始。");
        }

        var header = enumerator.Current;
        ValidateHeader(header, expectedLeaseId);
        using var reservation = await _resources.ReserveAsync(header.ExpectedContentBytes!.Value, token);
        using var content = new MemoryStream(checked((int)header.ExpectedContentBytes.Value));
        var expectedIndex = 0;
        WindowSnapshotFrame? trailer = null;

        while (await enumerator.MoveNextAsync())
        {
            var frame = enumerator.Current;
            if (!string.Equals(frame.SnapshotId, header.SnapshotId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("快照帧的 SnapshotId 不一致。");
            }

            if (frame.Kind == WindowSnapshotFrameKind.Trailer)
            {
                trailer = frame;
                break;
            }

            if (frame.Kind != WindowSnapshotFrameKind.Chunk
                || frame.Index != expectedIndex++
                || frame.Payload is null
                || frame.Payload.Length is <= 0 or > 16 * 1024)
            {
                throw new InvalidDataException("快照 Chunk 帧无效或顺序不连续。");
            }

            if (content.Length + frame.Payload.Length > header.ExpectedContentBytes)
            {
                throw new InvalidDataException("快照实际内容超过 Header 声明长度。");
            }

            await content.WriteAsync(frame.Payload, token);
        }

        if (trailer is null || await enumerator.MoveNextAsync())
        {
            throw new InvalidDataException("快照必须且只能包含一个 Trailer 结束帧。");
        }

        var bytes = content.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (trailer.ActualContentBytes != bytes.LongLength
            || trailer.ActualWindowCount != header.ExpectedWindowCount
            || !string.Equals(trailer.ContentHash, hash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("快照 Trailer 校验失败。");
        }

        var windows = JsonSerializer.Deserialize<AgentWindow[]>(bytes, JsonOptions)
            ?? throw new InvalidDataException("快照内容为空。");
        ValidateWindows(windows, header.ExpectedWindowCount!.Value);
        return new WindowSnapshot(header.SnapshotId, header.SnapshotRevision!.Value, windows);
    }

    private void ValidateHeader(WindowSnapshotFrame header, string expectedLeaseId)
    {
        if (string.IsNullOrWhiteSpace(header.SnapshotId)
            || !string.Equals(header.LeaseId, expectedLeaseId, StringComparison.Ordinal)
            || header.ProtocolVersion != ProtocolVersion
            || header.SnapshotRevision is null
            || header.ExpectedWindowCount is null or < 0
            || header.ExpectedWindowCount > _limits.MaxPendingWindowCountPerSession
            || header.ExpectedContentBytes is null or <= 0
            || header.ExpectedContentBytes > _limits.MaxSnapshotBytesPerSession)
        {
            throw new InvalidDataException("快照 Header 声明无效。");
        }
    }

    private void ValidateWindows(AgentWindow[] windows, int expectedCount)
    {
        if (windows.Length != expectedCount)
        {
            throw new InvalidDataException("快照窗口数量与 Header 不一致。");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var window in windows)
        {
            if (string.IsNullOrWhiteSpace(window.WindowId)
                || !ids.Add(window.WindowId)
                || !string.Equals(window.Status, "pending", StringComparison.OrdinalIgnoreCase)
                || JsonSerializer.SerializeToUtf8Bytes(window, JsonOptions).Length > _limits.MaxSerializedWindowBytes)
            {
                throw new InvalidDataException("快照只允许包含 WindowId 唯一的 Pending 窗口。");
            }
        }

        var byId = windows.ToDictionary(item => item.WindowId, StringComparer.Ordinal);
        foreach (var window in windows.Where(item => item.OwnerWindowId is not null))
        {
            if (!window.Modal || string.Equals(window.WindowId, window.OwnerWindowId, StringComparison.Ordinal))
                throw new InvalidDataException("模态窗口的 OwnerWindowId 无效。");
            var cursor = window;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (cursor.OwnerWindowId is not null)
            {
                if (!visited.Add(cursor.WindowId))
                    throw new InvalidDataException("模态窗口关系存在循环。");
                // Owner 也可能是同一服务会话中的 BrowserWindow。流式页面快照本身
                // 无法判断这个外部根节点，待两类窗口均同步后再做统一校验。
                if (!byId.TryGetValue(cursor.OwnerWindowId, out var owner)) break;
                cursor = owner;
            }
        }
    }
}
