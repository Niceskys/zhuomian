namespace Zhuomian.Spike.FocusAndInput;

internal sealed record FocusAndInputEvidence(
    int SchemaVersion,
    DateTimeOffset TimestampUtc,
    string OsVersion,
    string ProcessArchitecture,
    bool ExternalForegroundWasAvailable,
    bool VisualSurfaceShownWithoutActivation,
    bool VisualSurfacePhysicalClickPreservedForeground,
    bool KeyboardSurfaceInitiallyDidNotActivate,
    bool ExplicitKeyboardClickAcquiredForeground,
    bool KeyboardSurfaceReceivedFocus,
    bool GuardedUnicodeInputWasDelivered,
    bool ProbeTokenWasReceived,
    bool EscapeWasHandled,
    bool KeyboardSurfaceWasClosed,
    bool OriginalForegroundWasRestored,
    bool PointerWasRestored,
    bool VisualSurfaceRemainedNoActivate,
    bool NoProbeWindowsRemained,
    string InputToken,
    string[] SafetyAborts,
    string[] Limitations,
    string[] FailedChecks)
{
    public bool Passed => FailedChecks.Length == 0 && SafetyAborts.Length == 0;
}
