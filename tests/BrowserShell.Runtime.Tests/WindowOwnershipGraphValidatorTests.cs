using BrowserShell.Service.SDK;
using BrowserShell.Runtime;

namespace BrowserShell.Runtime.Tests;

public sealed class WindowOwnershipGraphValidatorTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void PageWindow和BrowserWindow可以互相作为同会话父窗口()
    {
        var browserRoot = Browser("browser-root", null);
        var pageChild = Page("page-child", "browser-root");
        var pageRoot = Page("page-root", null, modal: false);
        var browserChild = Browser("browser-child", "page-root");

        WindowOwnershipGraphValidator.Validate(
            [pageChild, pageRoot], [browserRoot, browserChild]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void 不存在的跨类型父窗口被拒绝()
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            WindowOwnershipGraphValidator.Validate(
                [Page("page", "missing")], []));

        Assert.Contains("不存在于当前服务会话", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void 跨类型Owner循环被拒绝()
    {
        Assert.Throws<InvalidDataException>(() =>
            WindowOwnershipGraphValidator.Validate(
                [Page("page", "browser")], [Browser("browser", "page")]));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PageWindow和BrowserWindow不能使用重复窗口标识()
    {
        Assert.Throws<InvalidDataException>(() =>
            WindowOwnershipGraphValidator.Validate(
                [Page("same", null, modal: false)], [Browser("same", null)]));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void 任意深度Owner链不受固定层级限制()
    {
        var pages = Enumerable.Range(0, 64)
            .Select(index => Page($"page-{index}", index == 0 ? null : $"page-{index - 1}"))
            .ToArray();

        WindowOwnershipGraphValidator.Validate(pages, []);
        var ordered = WindowOwnershipGraphValidator.OrderPageWindowsParentsFirst(pages.Reverse().ToArray());

        Assert.Equal(pages.Select(window => window.WindowId), ordered.Select(window => window.WindowId));
    }

    private static AgentWindow Page(string id, string? owner, bool modal = true) =>
        new(id, "view", 1, modal, null, null, null, null,
            false, true, false, owner, "pending", 1, 1);

    private static AgentBrowserWindow Browser(string id, string? owner) =>
        new(id, owner, "https://example.com/", null, null, null, null, null,
            true, false, true, ["https://example.com"], 1);
}
