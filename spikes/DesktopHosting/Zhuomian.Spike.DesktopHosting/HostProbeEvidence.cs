namespace Zhuomian.Spike.DesktopHosting;

internal sealed record HostProbeEvidence(
    int SchemaVersion,
    DateTimeOffset TimestampUtc,
    string OsVersion,
    string ProcessArchitecture,
    int MonitorCount,
    bool ForegroundWasAvailable,
    bool ForegroundPreservedAfterShow,
    bool HostNeverBecameForeground,
    bool NoActivateStylePresent,
    bool ToolWindowStylePresent,
    bool TopMostStyleAbsent,
    bool MouseActivationReturnsNoActivate,
    bool BottomPlacementSucceeded,
    bool MonitorMappingSucceeded,
    bool WindowWasInsideMonitorWorkArea,
    bool WindowWasDestroyed,
    string HostMode,
    string[] Limitations,
    string[] FailedChecks)
{
    public bool Passed => FailedChecks.Length == 0;
}
