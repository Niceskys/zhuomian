using Zhuomian.Core.Interaction;

namespace Zhuomian.Core.Tests.Interaction;

public sealed class InteractionSafetyTests
{
    public static TheoryData<DesktopAvailability, DesktopIntentGate, ActivationMode, bool>
        HoverIntentCases => new()
        {
            { DesktopAvailability.DesktopAvailable, DesktopIntentGate.Disarmed, ActivationMode.Hover, true },
            { DesktopAvailability.DesktopAvailable, DesktopIntentGate.Armed, ActivationMode.Hover, true },
            { DesktopAvailability.ExternalForeground, DesktopIntentGate.Disarmed, ActivationMode.Hover, false },
            { DesktopAvailability.ExternalForeground, DesktopIntentGate.Armed, ActivationMode.Hover, true },
            { DesktopAvailability.Suspended, DesktopIntentGate.Disarmed, ActivationMode.Hover, false },
            { DesktopAvailability.Suspended, DesktopIntentGate.Armed, ActivationMode.Hover, false },
            { DesktopAvailability.DesktopAvailable, DesktopIntentGate.Armed, ActivationMode.Click, false },
        };

    [Theory]
    [MemberData(nameof(HoverIntentCases))]
    public void HoverIntentFollowsTheForegroundGate(
        DesktopAvailability availability,
        DesktopIntentGate intentGate,
        ActivationMode activationMode,
        bool expected)
    {
        var snapshot = InteractionSnapshot.SafeInitial with
        {
            Availability = availability,
            IntentGate = intentGate,
        };

        Assert.Equal(expected, InteractionSafety.CanHoverEnterIntent(snapshot, activationMode));
    }

    [Fact]
    public void SafeInitialStateDoesNotCaptureKeyboardOrSuppressCollapse()
    {
        var snapshot = InteractionSnapshot.SafeInitial;

        Assert.Equal(SpaceVisualState.Idle, snapshot.SpaceState);
        Assert.Equal(DesktopIntentGate.Disarmed, snapshot.IntentGate);
        Assert.False(InteractionSafety.CanCaptureKeyboard(snapshot));
        Assert.False(InteractionSafety.SuppressesAutoCollapse(snapshot));
    }

    [Fact]
    public void ExplicitKeyboardModeIsRequiredToCaptureTextInput()
    {
        var snapshot = InteractionSnapshot.SafeInitial with
        {
            KeyboardMode = KeyboardMode.ZhuomianKeyboardActive,
        };

        Assert.True(InteractionSafety.CanCaptureKeyboard(snapshot));
    }

    [Theory]
    [InlineData(InteractionActivity.Searching)]
    [InlineData(InteractionActivity.Scrolling)]
    [InlineData(InteractionActivity.ContextMenuOpen)]
    [InlineData(InteractionActivity.Dragging)]
    [InlineData(InteractionActivity.PointerCaptured)]
    [InlineData(InteractionActivity.Searching | InteractionActivity.PointerCaptured)]
    public void AnyActiveInteractionSuppressesAutoCollapse(InteractionActivity activities)
    {
        var snapshot = InteractionSnapshot.SafeInitial with { Activities = activities };

        Assert.True(InteractionSafety.SuppressesAutoCollapse(snapshot));
    }
}
