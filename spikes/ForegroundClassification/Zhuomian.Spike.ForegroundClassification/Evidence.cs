namespace Zhuomian.Spike.ForegroundClassification;

internal sealed record CheckEvidence(string Name, bool Passed);

internal sealed record ForegroundClassificationEvidence(
    int SchemaVersion,
    DateTimeOffset TimestampUtc,
    string OsVersion,
    string ProcessArchitecture,
    bool InputDesktopWasAccessible,
    string InitialExternalKind,
    string InitialExternalAvailability,
    int RealExternalSamples,
    int RealExternalBlockedSamples,
    bool LiveShellClassifiedAsDesktopAvailable,
    bool OwnForegroundClassifiedAsDesktopAvailable,
    bool OriginalForegroundWasRestored,
    bool PointerWasRestored,
    int TruthTableCases,
    int TruthTablePassedCases,
    int ExternalDisarmedHoverAttempts,
    int ExternalDisarmedExpansions,
    bool HoverGateHasNoKeyboardCapturePath,
    CheckEvidence[] SyntheticChecks,
    string[] SafetyAborts,
    string[] Limitations,
    string[] FailedChecks)
{
    public bool Passed => FailedChecks.Length == 0 && SafetyAborts.Length == 0;
}
