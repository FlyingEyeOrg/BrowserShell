using System.Text.Json;
using BrowserShell;

namespace BrowserShell.Runtime.Tests;

public sealed class WindowTitleBarOptionsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void InstanceValuesOverrideViewValuesPerField()
    {
        var resolved = WindowTitleBarOptionsResolver.Resolve(
            new WindowTitleBarOptions
            {
                ActiveBackground = "#010203",
                Border = "#10111213",
            },
            new WindowTitleBarOptions
            {
                ActiveForeground = "#AABBCC",
            });

        Assert.Equal("#010203", resolved.ActiveBackground);
        Assert.Equal("#AABBCC", resolved.ActiveForeground);
        Assert.Equal("#1F1F1F", resolved.InactiveBackground);
        Assert.Equal("#10111213", resolved.Border);
    }

    [Theory]
    [InlineData("181818")]
    [InlineData("#12345")]
    [InlineData("#GG0000")]
    [InlineData("#1122334455")]
    public void InvalidTokensAreRejected(string color) =>
        Assert.Throws<ArgumentException>(() => WindowTitleBarOptionsResolver.Resolve(
            null,
            new WindowTitleBarOptions { Border = color }));

    [Fact]
    public void OldProjectionWithoutTitleBarUsesNullForCompatibility()
    {
        const string json = """
            {"windowId":"w","viewName":"v","viewVersion":1,"modal":false,"title":null,
             "data":null,"width":null,"height":null,"topmost":false,"focus":true,"flash":false,
             "ownerWindowId":null,"status":"pending","createdAt":1,"revision":1}
            """;
        var projection = JsonSerializer.Deserialize<AgentWindow>(json, JsonOptions);
        Assert.NotNull(projection);
        Assert.Null(projection.TitleBar);
        var resolved = WindowTitleBarOptionsResolver.Resolve(null, projection.TitleBar);
        Assert.Equal("#181818", resolved.ActiveBackground);
    }
}
