namespace BrowserShell.Runtime.Tests;

public sealed class ModalWindowBlockStateTests
{
    [Fact]
    public void Parent_RemainsBlockedUntilEveryDirectModalChildIsReleased()
    {
        var state = new ModalWindowBlockState();

        Assert.True(state.Add());
        Assert.False(state.Add());
        Assert.False(state.Remove());

        Assert.True(state.IsBlocked);
        Assert.Equal(1, state.Count);

        Assert.True(state.Remove());

        Assert.False(state.IsBlocked);
        Assert.Equal(0, state.Count);
    }

    [Fact]
    public void Remove_IsIdempotentAfterStateIsUnblocked()
    {
        var state = new ModalWindowBlockState();

        Assert.False(state.Remove());

        Assert.False(state.IsBlocked);
        Assert.Equal(0, state.Count);
    }

    [Fact]
    public void Reset_ReportsOnlyAnActualBlockedToEnabledTransition()
    {
        var state = new ModalWindowBlockState();

        Assert.False(state.Reset());
        Assert.True(state.Add());
        Assert.True(state.Reset());
        Assert.False(state.Reset());
        Assert.False(state.IsBlocked);
    }
}
