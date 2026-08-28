namespace Zhuomian.Spike.FallbackVisualUsability;

internal sealed record VisualUsabilityEvidence(
    int SchemaVersion,
    DateTimeOffset TimestampUtc,
    string OsVersion,
    string ProcessArchitecture,
    int MonitorCount,
    string Strategy,
    bool Passed,
    bool HostVisibleOnExposedDesktop,
    bool OrdinaryWindowCoveredHost,
    bool PreviewItemSingleClickExecuted,
    bool PreviewClickPreservedExternalForeground,
    bool TransparentAreaPassedThrough,
    bool HostWasBorderless,
    bool HostWasNotTopMost,
    bool HostWasNoActivate,
    bool HostWasDestroyed,
    int PreviewExecutionCount,
    string ScreenshotArtifact,
    IReadOnlyDictionary<string, bool> Checks,
    IReadOnlyList<string> FailedChecks,
    IReadOnlyList<string> Limitations);
