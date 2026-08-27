namespace Zhuomian.Spike.MultiMonitorDpi;

internal sealed record MonitorEvidence(
    int Index,
    bool IsPrimary,
    string StableKeyHash,
    int MonitorWidthPx,
    int MonitorHeightPx,
    int WorkAreaWidthPx,
    int WorkAreaHeightPx,
    uint Dpi,
    int ScalePercent,
    bool PerMonitorAware,
    bool HostMappedToExpectedMonitor,
    bool HostMatchedWorkArea,
    bool HostWasBorderless,
    bool ClientAreaMatchedWindow,
    bool HostWasDestroyed);

internal sealed record SyntheticCheckEvidence(string Name, bool Passed);

internal sealed record MultiMonitorDpiEvidence(
    int SchemaVersion,
    DateTimeOffset TimestampUtc,
    string OsVersion,
    string ProcessArchitecture,
    bool ProcessPerMonitorV2,
    int MonitorCount,
    int DistinctDpiCount,
    bool MultipleMonitorHardwareCoverage,
    bool MixedDpiHardwareCoverage,
    bool ActualPerMonitorHostsPassed,
    bool SyntheticMixedDpiMappingPassed,
    MonitorEvidence[] Monitors,
    SyntheticCheckEvidence[] SyntheticChecks,
    string[] CoverageGaps,
    string[] Limitations,
    string[] FailedChecks)
{
    public bool Passed => FailedChecks.Length == 0;

    public bool CoverageComplete => Passed && MultipleMonitorHardwareCoverage && MixedDpiHardwareCoverage;
}
