namespace Zhuomian.Spike.EnhancedHosting;

internal sealed record PolicyCheckEvidence(string Name, bool Passed);

internal sealed record EnhancedHostingEvidence(
    int SchemaVersion,
    DateTimeOffset TimestampUtc,
    string OsVersion,
    string ProcessArchitecture,
    int WorkerWCountBeforeRequest,
    int VisibleWorkerWCountBeforeRequest,
    int ViableWorkerWCountBeforeRequest,
    bool ProgmanContainedDesktopView,
    bool PrivateWorkerRequestAttempted,
    bool PrivateWorkerRequestDelivered,
    int WorkerWCountAfterRequest,
    int VisibleWorkerWCountAfterRequest,
    int ViableWorkerWCountAfterRequest,
    bool PrivateRequestProducedViableCandidate,
    bool EnhancedCandidateAccepted,
    bool CrossProcessSetParentAttempted,
    string SelectedHostMode,
    bool AutomaticFallbackOccurred,
    bool FallbackShownWithoutActivation,
    bool FallbackWasNoActivate,
    bool FallbackWasNotTopMost,
    bool FallbackWasBorderless,
    bool FallbackClientAreaMatchedWindow,
    bool FallbackMatchedWorkArea,
    bool FallbackDpiWasAvailable,
    bool FallbackWasDestroyed,
    PolicyCheckEvidence[] PolicyChecks,
    string[] EnhancedRejectionReasons,
    string[] Limitations,
    string[] FailedChecks)
{
    public bool Passed => FailedChecks.Length == 0;
}
