namespace Zhuomian.Spike.ExplorerRecovery;

internal sealed record ExplorerRecoveryEvidence(
    int SchemaVersion,
    DateTimeOffset TimestampUtc,
    string OsVersion,
    string ProcessArchitecture,
    bool InitialShellWasAvailable,
    bool InitialHostWasCreated,
    bool HostLostWasDetected,
    bool InitialHostWasDestroyedAfterLoss,
    bool TaskbarCreatedWasReceived,
    bool NewShellGenerationWasDetected,
    bool NewHostWasCreated,
    bool NewHostWasNoActivate,
    bool NewHostNeverBecameForeground,
    double RecoveryMilliseconds,
    bool RecoveryWasWithinFiveSeconds,
    bool InputCaptureWasReleased,
    bool AnimationCallbacksWereStopped,
    bool MediaResourcesWereReleased,
    string FinalDesktopAvailability,
    string FinalIntentGate,
    string FinalKeyboardMode,
    string FinalSpaceState,
    bool FinalStateWasSafeIdle,
    bool ExplorerWasAvailableAtEnd,
    bool AllProbeWindowsWereDestroyed,
    string[] Limitations,
    string[] FailedChecks)
{
    public bool Passed => FailedChecks.Length == 0;
}
