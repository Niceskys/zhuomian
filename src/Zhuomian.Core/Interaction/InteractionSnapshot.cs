namespace Zhuomian.Core.Interaction;

public readonly record struct InteractionSnapshot(
    DesktopAvailability Availability,
    DesktopIntentGate IntentGate,
    KeyboardMode KeyboardMode,
    SpaceVisualState SpaceState,
    InteractionActivity Activities)
{
    public static InteractionSnapshot SafeInitial { get; } = new(
        DesktopAvailability.DesktopAvailable,
        DesktopIntentGate.Disarmed,
        KeyboardMode.NoKeyboardCapture,
        SpaceVisualState.Idle,
        InteractionActivity.None);
}
