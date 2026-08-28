namespace Zhuomian.Spike.PlatformLifecycle;

internal enum DesktopAvailability
{
    DesktopAvailable,
    ExternalForeground,
    Suspended,
}

internal sealed record RuntimeState(
    DesktopAvailability Availability,
    bool Armed,
    bool KeyboardActive,
    bool PointerCaptured,
    bool AnimationActive,
    bool MediaOwned,
    bool SpaceIdle,
    bool AwaitingDesktopReady)
{
    internal static RuntimeState BusyExternal() => new(
        DesktopAvailability.ExternalForeground,
        Armed: true,
        KeyboardActive: true,
        PointerCaptured: true,
        AnimationActive: true,
        MediaOwned: true,
        SpaceIdle: false,
        AwaitingDesktopReady: false);
}

internal static class LifecyclePolicy
{
    internal static RuntimeState Suspend(RuntimeState state) => state with
    {
        Availability = DesktopAvailability.Suspended,
        Armed = false,
        KeyboardActive = false,
        PointerCaptured = false,
        AnimationActive = false,
        MediaOwned = false,
        SpaceIdle = true,
        AwaitingDesktopReady = true,
    };

    internal static RuntimeState ObserveUnlock(RuntimeState state) => state with
    {
        Availability = DesktopAvailability.Suspended,
        Armed = false,
        KeyboardActive = false,
        PointerCaptured = false,
        AnimationActive = false,
        MediaOwned = false,
        SpaceIdle = true,
        AwaitingDesktopReady = true,
    };

    internal static RuntimeState ObserveDesktopReady(RuntimeState state, bool defaultDesktopAccessible) =>
        defaultDesktopAccessible
            ? state with
            {
                Availability = DesktopAvailability.ExternalForeground,
                Armed = false,
                KeyboardActive = false,
                PointerCaptured = false,
                AnimationActive = false,
                MediaOwned = false,
                SpaceIdle = true,
                AwaitingDesktopReady = false,
            }
            : ObserveUnlock(state);

    internal static bool IsSafelySuspended(RuntimeState state) =>
        state.Availability == DesktopAvailability.Suspended &&
        !state.Armed &&
        !state.KeyboardActive &&
        !state.PointerCaptured &&
        !state.AnimationActive &&
        !state.MediaOwned &&
        state.SpaceIdle &&
        state.AwaitingDesktopReady;
}
