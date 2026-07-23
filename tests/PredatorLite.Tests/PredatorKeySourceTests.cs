using PredatorLite.Platform.Windows.SystemIntegration;

namespace PredatorLite.Tests;

public sealed class PredatorKeySourceTests
{
    [Fact]
    public void UnrelatedScanCodeIsPassedThrough()
    {
        PredatorKeyState state = new();

        PredatorKeyDecision decision = state.Handle(
            PredatorKeyState.KeyDownMessage,
            virtualKey: 0,
            scanCode: 0x74,
            flags: 0);

        Assert.False(decision.ShouldSuppress);
        Assert.False(decision.ShouldActivate);
    }

    [Fact]
    public void InitialKeyDownIsSuppressedWithoutActivation()
    {
        PredatorKeyState state = new();

        PredatorKeyDecision decision = Press(state);

        Assert.True(decision.ShouldSuppress);
        Assert.False(decision.ShouldActivate);
    }

    [Fact]
    public void RepeatedKeyDownDoesNotActivate()
    {
        PredatorKeyState state = new();
        Press(state);

        PredatorKeyDecision decision = Press(state);

        Assert.True(decision.ShouldSuppress);
        Assert.False(decision.ShouldActivate);
    }

    [Fact]
    public void KeyUpActivatesOnlyOnceAfterKeyDown()
    {
        PredatorKeyState state = new();
        Press(state);

        PredatorKeyDecision firstRelease = Release(state);
        PredatorKeyDecision secondRelease = Release(state);

        Assert.True(firstRelease.ShouldSuppress);
        Assert.True(firstRelease.ShouldActivate);
        Assert.True(secondRelease.ShouldSuppress);
        Assert.False(secondRelease.ShouldActivate);
    }

    [Fact]
    public void IsolatedKeyUpIsSuppressedWithoutActivation()
    {
        PredatorKeyState state = new();

        PredatorKeyDecision decision = Release(state);

        Assert.True(decision.ShouldSuppress);
        Assert.False(decision.ShouldActivate);
    }

    [Fact]
    public void SystemKeyMessagesUseTheSameGestureState()
    {
        PredatorKeyState state = new();

        PredatorKeyDecision press = state.Handle(
            PredatorKeyState.SystemKeyDownMessage,
            virtualKey: 0,
            PredatorKeyState.PredatorScanCode,
            flags: 0);
        PredatorKeyDecision release = state.Handle(
            PredatorKeyState.SystemKeyUpMessage,
            virtualKey: 0,
            PredatorKeyState.PredatorScanCode,
            flags: 0);

        Assert.True(press.ShouldSuppress);
        Assert.False(press.ShouldActivate);
        Assert.True(release.ShouldSuppress);
        Assert.True(release.ShouldActivate);
    }

    [Theory]
    [InlineData(PredatorKeyState.LowerIntegrityInjectedFlag)]
    [InlineData(PredatorKeyState.InjectedFlag)]
    public void InjectedEventIsPassedThrough(uint flags)
    {
        PredatorKeyState state = new();

        PredatorKeyDecision decision = state.Handle(
            PredatorKeyState.KeyDownMessage,
            virtualKey: 0,
            PredatorKeyState.PredatorScanCode,
            flags);

        Assert.False(decision.ShouldSuppress);
        Assert.False(decision.ShouldActivate);
    }

    [Fact]
    public void UnicodePacketEventIsPassedThrough()
    {
        PredatorKeyState state = new();

        PredatorKeyDecision decision = state.Handle(
            PredatorKeyState.KeyDownMessage,
            PredatorKeyState.PacketVirtualKey,
            PredatorKeyState.PredatorScanCode,
            flags: 0);

        Assert.False(decision.ShouldSuppress);
        Assert.False(decision.ShouldActivate);
    }

    private static PredatorKeyDecision Press(PredatorKeyState state) =>
        state.Handle(
            PredatorKeyState.KeyDownMessage,
            virtualKey: 0,
            PredatorKeyState.PredatorScanCode,
            flags: 0);

    private static PredatorKeyDecision Release(PredatorKeyState state) =>
        state.Handle(
            PredatorKeyState.KeyUpMessage,
            virtualKey: 0,
            PredatorKeyState.PredatorScanCode,
            flags: 0);
}
