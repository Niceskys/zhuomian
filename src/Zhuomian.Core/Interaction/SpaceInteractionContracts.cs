namespace Zhuomian.Core.Interaction;

public sealed record SpaceActivationPolicy
{
    public SpaceActivationPolicy(ActivationMode mode, int hoverDelayMilliseconds = 20)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(hoverDelayMilliseconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hoverDelayMilliseconds, 120);
        Mode = mode;
        HoverDelayMilliseconds = hoverDelayMilliseconds;
    }

    public ActivationMode Mode { get; }
    public int HoverDelayMilliseconds { get; }
}

/// <summary>Only a stable identity crosses this boundary, never a path or command line.</summary>
public interface IInteractionLauncher
{
    bool TryLaunch(string itemId);
}

public enum SpaceInteractionEventKind
{
    AvailabilityChanged,
    ExternalInput,
    HostLost,
    HostRestored,
    DragEnter,
    DragEnd,
    PointerEnter,
    PointerLeave,
    PointerDown,
    PointerUp,
    PointerCancel,
    VisibleDesktopClick,
    ExplicitExpand,
    Close,
    TransitionCompleted,
    ActivityChanged,
    ModalOpened,
    ModalClosed,
    SearchActivate,
    TextInput,
    ImeStarted,
    ImeEnded,
    Escape,
    ItemInvalidated,
    SpaceRemoved,
    LayoutChanged,
    Deadline,
}

/// <summary>
/// Adapter-normalized input. GestureId increases for each physical click sequence; OS double-click
/// follow-ups retain the first ID or use ClickCount=2. PointerUp carries the pinned item identity
/// and ValidClick from click recognition, not a new blank hit test after layout movement.
/// TransitionCompleted/Deadline must echo the exact issued Token. TextInput and IME events
/// echo KeyboardToken from the explicit search session. Asynchronous input/hit snapshots
/// capture InputGeneration before dispatch and echo Generation; null denotes synchronous dispatcher input. All identities are opaque, not launch payloads.
/// </summary>
public sealed record SpaceInteractionEvent(
    SpaceInteractionEventKind Kind,
    string? SpaceId = null,
    string? ItemId = null,
    long GestureId = 0,
    long Token = 0,
    bool ValidClick = true,
    int ClickCount = 1,
    DesktopAvailability Availability = DesktopAvailability.DesktopAvailable,
    string? ForegroundIdentity = null,
    InteractionActivity Activities = InteractionActivity.None,
    string Text = "",
    long? Generation = null);

public readonly record struct SpaceInteractionState(
    SpaceVisualState VisualState,
    InteractionActivity Activities,
    bool PointerInside,
    long TransitionToken);

public enum InteractionOutcomeKind
{
    LaunchSucceeded,
    LaunchFailed,
    ClickInvalidated,
    EscapeConsumed,
    EscapeUnhandled,
}

public readonly record struct InteractionOutcome(InteractionOutcomeKind Kind, string? ItemId = null);
