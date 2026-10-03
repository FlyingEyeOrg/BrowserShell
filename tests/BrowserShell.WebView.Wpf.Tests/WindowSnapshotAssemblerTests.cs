using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using BrowserShell.Service.SDK;
using BrowserShell.WebView.Wpf;

namespace BrowserShell.WebView.Wpf.Tests;

public sealed class WindowSnapshotAssemblerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    [Fact]
    public async Task 正确快照可以完成原子组装()
    {
        var window = new AgentWindow("window-1", "view", 1, false, null, null, null, null,
            false, true, false, null, "pending", 1, 1);
        var frames = CreateFrames("lease", [window]);
        using var resources = new SnapshotResourceCoordinator(new RuntimeSettings());
        var assembler = new WindowSnapshotAssembler(resources, new RuntimeSettings());

        var snapshot = await assembler.AssembleAsync(Enumerate(frames), "lease", CancellationToken.None);

        Assert.Single(snapshot.Windows);
        Assert.Equal("window-1", snapshot.Windows[0].WindowId);
    }

    [Fact]
    public async Task 乱序分片被拒绝()
    {
        var frames = CreateFrames("lease", []);
        frames[1] = frames[1] with { Index = 2 };
        using var resources = new SnapshotResourceCoordinator(new RuntimeSettings());
        var assembler = new WindowSnapshotAssembler(resources, new RuntimeSettings());

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            assembler.AssembleAsync(Enumerate(frames), "lease", CancellationToken.None));
    }

    [Fact]
    public async Task 模态Owner循环被拒绝()
    {
        var first = new AgentWindow("first", "view", 1, true, null, null, null, null,
            false, true, false, "second", "pending", 1, 1);
        var second = new AgentWindow("second", "view", 1, true, null, null, null, null,
            false, true, false, "first", "pending", 2, 1);
        using var resources = new SnapshotResourceCoordinator(new RuntimeSettings());
        var assembler = new WindowSnapshotAssembler(resources, new RuntimeSettings());

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            assembler.AssembleAsync(Enumerate(CreateFrames("lease", [first, second])), "lease", CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BrowserWindow作为Owner时页面快照允许外部根节点等待统一校验()
    {
        var window = new AgentWindow("page", "view", 1, true, null, null, null, null,
            false, true, false, "browser", "pending", 1, 1);
        using var resources = new SnapshotResourceCoordinator(new RuntimeSettings());
        var assembler = new WindowSnapshotAssembler(resources, new RuntimeSettings());

        var snapshot = await assembler.AssembleAsync(
            Enumerate(CreateFrames("lease", [window])), "lease", CancellationToken.None);

        Assert.Equal("browser", Assert.Single(snapshot.Windows).OwnerWindowId);
    }

    [Fact]
    public async Task 超过八级的合法模态链可以组装()
    {
        var windows = Enumerable.Range(0, 32)
            .Select(index => new AgentWindow($"window-{index}", "view", 1, true, null, null, null, null,
                false, true, false, index == 0 ? null : $"window-{index - 1}", "pending", index, 1))
            .ToArray();
        using var resources = new SnapshotResourceCoordinator(new RuntimeSettings());
        var assembler = new WindowSnapshotAssembler(resources, new RuntimeSettings());

        var snapshot = await assembler.AssembleAsync(
            Enumerate(CreateFrames("lease", windows)), "lease", CancellationToken.None);

        Assert.Equal(32, snapshot.Windows.Count);
    }

    private static List<WindowSnapshotFrame> CreateFrames(string lease, AgentWindow[] windows)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(windows, JsonOptions);
        var id = Guid.NewGuid().ToString("N");
        return
        [
            new(WindowSnapshotFrameKind.Header, id, lease, 1, 1, windows.Length, bytes.Length),
            new(WindowSnapshotFrameKind.Chunk, id, Index: 0, Payload: bytes),
            new(WindowSnapshotFrameKind.Trailer, id, ActualWindowCount: windows.Length,
                ActualContentBytes: bytes.Length,
                ContentHash: Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()),
        ];
    }

    private static async IAsyncEnumerable<WindowSnapshotFrame> Enumerate(
        IEnumerable<WindowSnapshotFrame> frames,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        foreach (var frame in frames)
        {
            token.ThrowIfCancellationRequested();
            yield return frame;
            await Task.Yield();
        }
    }
}
