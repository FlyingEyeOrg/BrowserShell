namespace BrowserShell.WebView.Wpf.Tests;

public sealed class ModalWindowReleaseTransitionTests
{
    [Fact]
    public void Run_ReleasesOwnerThenHidesRestoresForegroundAndDetaches()
    {
        var calls = new List<string>();

        ModalWindowReleaseTransition.Run(
            () => calls.Add("release-owner"),
            () => calls.Add("hide-child"),
            () => calls.Add("restore-foreground"),
            () => calls.Add("detach-owner"));

        Assert.Equal(["release-owner", "hide-child", "restore-foreground", "detach-owner"], calls);
    }

    [Fact]
    public void Run_DetachesOwnerWhenHidingFails()
    {
        var calls = new List<string>();

        Assert.Throws<InvalidOperationException>(() => ModalWindowReleaseTransition.Run(
            () => calls.Add("release-owner"),
            () => throw new InvalidOperationException("hide failed"),
            () => calls.Add("restore-foreground"),
            () => calls.Add("detach-owner")));

        Assert.Equal(["release-owner", "detach-owner"], calls);
    }
}
