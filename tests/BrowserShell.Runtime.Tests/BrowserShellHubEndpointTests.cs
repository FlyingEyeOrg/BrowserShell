using BrowserShell.Runtime;

namespace BrowserShell.Runtime.Tests;

public sealed class BrowserShellHubEndpointTests
{
    [Theory]
    [InlineData("http://127.0.0.1:5010/")]
    [InlineData("https://127.0.0.1:443/")]
    [InlineData("http://[::1]:5010/")]
    public void ExplicitLoopbackOriginsAreAccepted(string value) =>
        Assert.True(BrowserShellHub.IsAllowedServiceEndpoint(value));

    [Theory]
    [InlineData("http://localhost:5010/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://127.0.0.1:5010/path")]
    [InlineData("http://127.0.0.1:5010/?query=1")]
    [InlineData("http://user@127.0.0.1:5010/")]
    [InlineData("http://192.168.1.10:5010/")]
    [InlineData("http://0.0.0.0:5010/")]
    public void NonCanonicalOrNonLoopbackOriginsAreRejected(string value) =>
        Assert.False(BrowserShellHub.IsAllowedServiceEndpoint(value));
}
